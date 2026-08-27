using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Salience;

namespace Lyntai.Benchmarks;

/// <summary>
/// The instruments a sweep needs that are not the sweep itself — a real embedding model, its cache, and the
/// salience counter that proves an arm did what its name claims.
///
/// <para><b>Why they are here rather than in each sweep.</b> <c>CountingSaliencePolicy</c> was written twice,
/// privately, in two sweeps that measure the same signal, and a third copy was one file away when this was
/// extracted. <c>.claude/knowledge/pitfalls.md</c> records where that ends: one stored value read at N sites
/// grows N coercion rules, and the divergence is silent. An instrument is worse than ordinary code for this,
/// because its output is a NUMBER and a number is never obviously wrong — two counters that disagree about
/// what "salient" means produce two believable tables.</para>
///
/// <para><b>These are measurement doubles, not library surface.</b> Nothing here ships; the bench project is
/// where a study's scaffolding lives, and hoisting it does not make it an API.</para>
/// </summary>
internal static class SweepDoubles
{
    /// <summary>Environment variable naming the embedding model, so a machine with a different one pulled
    /// does not need a code change.</summary>
    internal const string ModelVariable = "LYNTAI_OLLAMA_EMBED_MODEL";

    /// <summary>Environment variable naming the endpoint. The legacy <c>LYNTAI_OLLAMA_URL</c> still works, so
    /// a machine already set up does not start failing because a name changed.</summary>
    internal const string UrlVariable = "LYNTAI_LIVE_MODEL_URL";

    /// <summary>The model this resolves to, for a preamble to print.</summary>
    internal static string Model =>
        Environment.GetEnvironmentVariable(ModelVariable) ?? "nomic-embed-text";

    /// <summary>The endpoint this resolves to.</summary>
    internal static string BaseUrl =>
        Environment.GetEnvironmentVariable(UrlVariable)
        ?? Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL")
        ?? "http://localhost:11434";

    /// <summary>
    /// A cached real embedder, or <c>null</c> when no model is reachable — in which case the refusal has
    /// already been written to stderr and the caller should return a non-zero exit.
    ///
    /// <para><b>It refuses rather than substituting a double, and that is the whole point.</b> The numbers a
    /// fake embedder produced were withdrawn (<c>TASKS.md</c> Part 69) because its "semantic similarity" is
    /// word overlap — so falling back here would reproduce, silently, the exact defect that withdrew
    /// them.</para>
    /// </summary>
    /// <param name="http">The client to use; the caller owns its lifetime.</param>
    /// <param name="sweep">The sweep's own name, so the refusal says which run stopped.</param>
    internal static async Task<CachingEmbedder?> TryRealEmbedderAsync(HttpClient http, string sweep)
    {
        var model = Model;
        var baseUrl = BaseUrl;
        var real = new OpenAiCompatibleEmbedder(http, baseUrl, model);
        if (await real.ReachableAsync()) return new CachingEmbedder(real);

        Console.Error.WriteLine($"{sweep}: ✗ no embedding model at {baseUrl} ({model}).");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  A fake embedder's \"semantic similarity\" is word overlap, and the numbers");
        Console.Error.WriteLine("  taken through one were withdrawn (TASKS.md Part 69). Substituting one here");
        Console.Error.WriteLine("  would reproduce that defect silently, so this refuses to run instead.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  Any OpenAI-compatible /v1/embeddings endpoint serves this:");
        Console.Error.WriteLine($"    - Ollama:      ollama pull {model}");
        Console.Error.WriteLine($"    - llama.cpp:   llama-server -m <model.gguf> --embedding");
        Console.Error.WriteLine($"  Point it with {UrlVariable}, and name the model with {ModelVariable}.");
        return null;
    }

    /// <summary>
    /// Counts how many writes salience judged notable — the control that separates "this arm's signal did
    /// nothing" from "this arm's signal never fired".
    ///
    /// <para><b><see cref="Judged"/> is what makes the control complete, and it was added because presence
    /// alone is not enough.</b> A knob that SCALES a signal is unmeasurable when that signal is constant
    /// across candidates, and constant has two shapes: nobody is salient, and EVERYONE is. Ranking by
    /// competition (<c>docs/DECISIONS.md</c> D82) gives a uniformly-tied signal the same contribution at
    /// every weight, so either extreme produces a perfectly flat curve that reads as a clean exoneration.
    /// A study over such a knob must report <see cref="Salient"/> AGAINST <see cref="Judged"/> and say so
    /// when the ratio is 0 or 1 — see <c>.claude/knowledge/pitfalls.md</c>, which records this trap being
    /// walked into deliberately and caught only by asking what the arms actually differed in.</para>
    /// </summary>
    /// <param name="inner">The policy being counted; null takes the shipped
    /// <see cref="StructuralSaliencePolicy"/>, which is what every sweep predating <c>memory-importance</c>
    /// measured. A study whose ARMS are different policies wraps each one, so the same definition of
    /// "salient" and "distinct" applies to all of them — two counters disagreeing about that produce two
    /// believable tables, which is the whole reason this class was hoisted here.</param>
    internal sealed class CountingSaliencePolicy(IMemorySaliencePolicy? inner = null) : IMemorySaliencePolicy
    {
        private readonly IMemorySaliencePolicy _inner = inner ?? new StructuralSaliencePolicy();
        private readonly ConcurrentDictionary<double, byte> _values = new();
        private int _salient;
        private int _judged;

        /// <summary>Writes this policy scored as notable.</summary>
        public int Salient => Volatile.Read(ref _salient);

        /// <summary>Writes it was asked about at all — the denominator <see cref="Salient"/> is only
        /// interpretable against.</summary>
        public int Judged => Volatile.Read(ref _judged);

        /// <summary>
        /// How many DISTINCT salience values were produced — the control a knob that SCALES this signal
        /// actually needs.
        ///
        /// <para><b><see cref="Salient"/> is the weaker question and it flatters.</b> Measured 2026-08-23:
        /// salience fired on 98.9% of writes, which passes any "did the signal appear" test while being
        /// nearly uniform on that axis — and a signal every candidate ties on contributes the same constant
        /// at every weight (D82). Firing is not varying. Read through
        /// <see cref="MemorySignals.Salience"/> rather than off the bag, because that is the one coercion
        /// every reader of this value is required to use.</para>
        /// </summary>
        public int DistinctValues => _values.Count;

        public MemorySalienceProvenance Provenance => _inner.Provenance;

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
        {
            var signals = _inner.Signals(write, in context);
            Interlocked.Increment(ref _judged);
            if (signals.Count > 0)
            {
                Interlocked.Increment(ref _salient);
                _values.TryAdd(MemorySignals.Salience(in signals), 0);
            }
            return signals;
        }
    }

    /// <summary>
    /// A real embedding model over the OpenAI-compatible <c>/v1/embeddings</c> route.
    ///
    /// <para><b>That route rather than Ollama's native one, so a sweep is not tied to a vendor.</b> Ollama
    /// and llama.cpp's <c>llama-server</c> both serve it; only Ollama serves <c>/api/embeddings</c>. Through
    /// 2026-08-26 this spoke the native dialect, which is why the two real-model sweeps could run against
    /// exactly one backend — and why a machine running llama-server instead saw them refuse with "no
    /// embedding model" while a perfectly good one was loaded.</para>
    /// </summary>
    /// <remarks>
    /// Written here rather than reusing <c>HttpEmbedder</c> so the bench project keeps its two project
    /// references — the csproj records what pulling a third one cost the last time (a build log past Node's
    /// spawnSync buffer, reported as a failed build that had in fact succeeded).
    /// </remarks>
    internal sealed class OpenAiCompatibleEmbedder(HttpClient http, string baseUrl, string model) : IEmbedder
    {
        /// <summary>
        /// Probes by actually EMBEDDING something, rather than by reading a model list.
        ///
        /// <para><b>Stronger than the tag-list check it replaces, and portable where that was not.</b> The
        /// old probe read Ollama's <c>/api/tags</c> and looked for the model's name — which llama-server has
        /// no equivalent of, and which answers a weaker question anyway: a listed model can still fail to
        /// embed. One round trip proves reachability, the model, and that a usable vector comes back, which
        /// is the whole of what the caller needs to know before spending minutes.</para>
        /// </summary>
        public async Task<bool> ReachableAsync()
        {
            try
            {
                var probe = await EmbedAsync(["probe"]);
                return probe.Count == 1 && probe[0].Length > 0;
            }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) { return false; }
            catch (JsonException) { return false; }        // answered, but not with an embedding
            catch (KeyNotFoundException) { return false; }
        }

        /// <remarks>
        /// One request per text. The route takes a batch, but the caching wrapper is what keeps the call
        /// count down, so there is nothing to win by being clever — and one text per call keeps the response
        /// shape identical on every backend.
        /// </remarks>
        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            var result = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                using var response = await http.PostAsJsonAsync($"{baseUrl}/v1/embeddings",
                    new { model, input = texts[i] }, ct);
                response.EnsureSuccessStatusCode();

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var vector = json.RootElement.GetProperty("data")[0].GetProperty("embedding");
                var one = new float[vector.GetArrayLength()];
                var j = 0;
                foreach (var value in vector.EnumerateArray()) one[j++] = (float)value.GetDouble();
                result[i] = one;
            }
            return result;
        }
    }

    /// <summary>Memoizes a real model by text — deterministic input, deterministic output.</summary>
    internal sealed class CachingEmbedder(IEmbedder inner) : IEmbedder
    {
        private readonly ConcurrentDictionary<string, Task<float[]>> _cache = new(StringComparer.Ordinal);
        private int _hits;
        private int _misses;

        public int Hits => Volatile.Read(ref _hits);
        public int Misses => Volatile.Read(ref _misses);

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            var result = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                var text = texts[i];
                if (_cache.TryGetValue(text, out var cached)) Interlocked.Increment(ref _hits);
                else
                {
                    Interlocked.Increment(ref _misses);
                    // GetOrAdd may still lose a race and run the factory twice; the value is deterministic,
                    // so a duplicate call costs one embed and never a wrong vector.
                    cached = _cache.GetOrAdd(text, t => inner.EmbedAsync(t, ct));
                }
                result[i] = await cached.ConfigureAwait(false);
            }
            return result;
        }
    }

    /// <summary>Environment variable naming the chat model an arm should ask.</summary>
    internal const string ChatModelVariable = "LYNTAI_LIVE_CHAT_MODEL";

    /// <summary>The chat model this resolves to.</summary>
    internal static string ChatModel =>
        Environment.GetEnvironmentVariable(ChatModelVariable) ?? "gemma3:4b";

    /// <summary>
    /// A real chat model over the OpenAI-compatible <c>/v1/chat/completions</c> route, or <c>null</c> when
    /// none is reachable — in which case the refusal is already on stderr and the caller exits non-zero.
    /// </summary>
    internal static async Task<OpenAiCompatibleChat?> TryRealChatAsync(HttpClient http, string sweep)
    {
        var model = ChatModel;
        var chat = new OpenAiCompatibleChat(http, BaseUrl, model);
        if (await chat.ReachableAsync()) return chat;

        Console.Error.WriteLine($"{sweep}: ✗ no chat model at {BaseUrl} ({model}).");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  This arm measures what a MODEL is worth, so a scripted stand-in would");
        Console.Error.WriteLine("  measure the stand-in. It refuses to run instead.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Any OpenAI-compatible /v1/chat/completions endpoint serves this:");
        Console.Error.WriteLine("    - llama.cpp:   llama-server -hf <user>/<model>[:quant]   (preferred)");
        Console.Error.WriteLine($"    - Ollama:      ollama pull {model}                        (convenience only)");
        Console.Error.WriteLine($"  Point it with {UrlVariable}, and name the model with {ChatModelVariable}.");
        return null;
    }

    /// <summary>A real chat model over the OpenAI-compatible route, asked one question at a time.</summary>
    internal sealed class OpenAiCompatibleChat(HttpClient http, string baseUrl, string model)
    {
        /// <summary>The model this instance asks, for a table to label its row with.</summary>
        public string Model => model;

        /// <summary>Probes by asking something, rather than by reading a model list — a listed model can
        /// still fail to answer, and llama-server has no tag list at all.</summary>
        public async Task<bool> ReachableAsync()
        {
            try { return await AskAsync("Reply with the single character: 1") is { Length: > 0 }; }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) { return false; }
            catch (JsonException) { return false; }
            catch (KeyNotFoundException) { return false; }
        }

        /// <remarks>Temperature 0 and a tiny cap: every question this asks has a one-token answer, and a
        /// model that wants to explain itself is spending latency the seam cannot afford.</remarks>
        public async Task<string?> AskAsync(string prompt, CancellationToken ct = default)
        {
            using var response = await http.PostAsJsonAsync($"{baseUrl}/v1/chat/completions",
                new
                {
                    model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0,
                    max_tokens = 4,
                }, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();
        }
    }
}
