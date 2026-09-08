using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// <summary>Environment variable naming the embedding model, so a machine serving a different one does
    /// not need a code change. The legacy <c>LYNTAI_OLLAMA_EMBED_MODEL</c> still works.
    /// <para><b>Named for the ROLE rather than for one server</b>, which the URL variable already was: these
    /// sweeps talk OpenAI-compatible HTTP and this machine runs both Ollama and <c>llama-server</c>, so a
    /// vendor in the name is a claim about the host that the code never makes.</para></summary>
    internal const string ModelVariable = "LYNTAI_LIVE_EMBED_MODEL";

    /// <summary>Environment variable naming the endpoint. The legacy <c>LYNTAI_OLLAMA_URL</c> still works, so
    /// a machine already set up does not start failing because a name changed.
    /// <para><b>The default is llama.cpp's own port, because llama.cpp is this repository's standard local
    /// server</b> (<c>repo-mechanics.md</c> §Local models). It used to be Ollama's <c>11434</c>, and that
    /// default is why every figure taken before 2026-09-08 is Ollama-served without any run having chosen
    /// it — nothing printed the endpoint, so the provenance had to be reconstructed afterwards from which
    /// processes happened to be up. Hence <see cref="TryRealEmbedderAsync"/> now prints what answered.</para>
    /// </summary>
    internal const string UrlVariable = "LYNTAI_LIVE_MODEL_URL";

    /// <summary>The model this resolves to, for a preamble to print.</summary>
    internal static string Model =>
        Environment.GetEnvironmentVariable(ModelVariable)
        ?? Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_EMBED_MODEL")
        ?? "nomic-embed-text";

    /// <summary>What actually ANSWERED, once <see cref="TryRealEmbedderAsync"/> has resolved an embedder —
    /// falling back to the requested name before that, or when the server names many models and so routes
    /// by the requested one. For a table HEADER, which is the one place the requested name reads as a
    /// finding rather than as a setting.</summary>
    internal static string ServedOrRequestedModel => _served ?? Model;

    private static string? _served;

    /// <summary>The endpoint this resolves to.</summary>
    internal static string BaseUrl =>
        Environment.GetEnvironmentVariable(UrlVariable)
        ?? Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_URL")
        ?? "http://localhost:8080";

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
        if (await real.ReachableAsync())
        {
            // PROVENANCE, printed on every run rather than reconstructed afterwards. The endpoint used to
            // appear nowhere — only the model NAME did — so a table said "embedder nomic-embed-text" and
            // could not say which of two servers answered it, and a whole session's figures had to be
            // attributed after the fact by asking which processes were up (`TASKS.md`, 2026-09-04).
            var served = _served = await real.ServedModelAsync();
            Console.WriteLine(served is null || served == model
                ? $"{sweep}: embedder {model} at {baseUrl}"
                : $"{sweep}: embedder {served} at {baseUrl} (requested {model}; the server serves what it loaded)");
            return new CachingEmbedder(real);
        }

        Console.Error.WriteLine($"{sweep}: ✗ no embedding model at {baseUrl} ({model}).");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  A fake embedder's \"semantic similarity\" is word overlap, and the numbers");
        Console.Error.WriteLine("  taken through one were withdrawn (TASKS.md Part 69). Substituting one here");
        Console.Error.WriteLine("  would reproduce that defect silently, so this refuses to run instead.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  Any OpenAI-compatible /v1/embeddings endpoint serves this:");
        Console.Error.WriteLine($"    - llama.cpp:   llama-server -m <model.gguf> --embedding   (the standard here)");
        Console.Error.WriteLine($"    - Ollama:      ollama pull {model}   (then set {UrlVariable})");
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

        /// <summary>What the SERVER reports it has loaded, or null when it reports anything other than
        /// exactly one model.
        /// <para><b>The requested name is not evidence of what answered.</b> A single-model
        /// <c>llama-server</c> ECHOES whatever model string it is sent and serves the file it was started
        /// with — measured: a request naming <c>whatever-name-is-ignored</c> comes back with that name and a
        /// real vector. So printing the requested name as provenance would restate the defect this print
        /// exists to fix. A catalogue server lists many models and routes by name, where the requested name
        /// IS the truth; that case returns null and the caller falls back to it.</para></summary>
        public async Task<string?> ServedModelAsync()
        {
            try
            {
                using var response = await http.GetAsync($"{baseUrl}/v1/models");
                if (!response.IsSuccessStatusCode) return null;
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                // "data" is the OpenAI shape; llama-server answers with "models"
                if (!json.RootElement.TryGetProperty("data", out var list) &&
                    !json.RootElement.TryGetProperty("models", out list)) return null;
                if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != 1) return null;

                var one = list[0];
                var name = one.TryGetProperty("id", out var id) ? id.GetString()
                    : one.TryGetProperty("name", out var n) ? n.GetString() : null;
                // a loaded GGUF is reported as a full path; the file name is the identifying half and the
                // only half that belongs in output someone may paste into a document
                return string.IsNullOrWhiteSpace(name) ? null : Path.GetFileName(name);
            }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
            catch (JsonException) { return null; }
        }

        /// <remarks>
        /// One request per text. The route takes a batch, but the caching wrapper is what keeps the call
        /// count down, so there is nothing to win by being clever — and one text per call keeps the response
        /// shape identical on every backend.
        /// </remarks>
        /// <summary>Characters a single input is cut to before being sent.
        ///
        /// <para><b>It is a cut this study was ALREADY making, invisibly.</b> An embedding model has a fixed
        /// context and LongMemEval's texts run to 76,560 characters against a median of 429. Ollama silently
        /// truncates an over-long input and answers; <c>llama-server</c> returns 500 — so switching servers
        /// turned a silent truncation into a crashed run, and the crash is what revealed that every figure
        /// on record was taken with the long tail quietly cut. Doing it here makes it COUNTED
        /// (<see cref="Truncated"/>) rather than a property of whichever server happened to answer.</para>
        ///
        /// <para>6,000 characters is ~1,200-1,500 tokens on this corpus, which clears both a 1536-token
        /// batch and a 2048-token context, so a run does not depend on how a server was started.</para>
        /// </summary>
        internal const int MaxInputChars = 6000;

        /// <summary>The floor the halving stops at, so a pathological input fails loudly rather than being
        /// cut to nothing and embedded as a meaningless vector.</summary>
        internal const int MinInputChars = 500;

        private static int _truncated;

        /// <summary>Inputs cut by <see cref="MaxInputChars"/>, process-wide. Non-zero is not a defect — it is
        /// the tail this corpus has — but a run that does not REPORT it is claiming to have embedded text it
        /// did not.</summary>
        internal static int Truncated => Volatile.Read(ref _truncated);

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default)
        {
            var result = new float[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                var input = texts[i];
                if (input.Length > MaxInputChars)
                {
                    input = input[..MaxInputChars];
                    Interlocked.Increment(ref _truncated);
                }

                // A CHARACTER budget cannot bound a TOKEN limit: density varies by an order of magnitude
                // across scripts, so one constant is either wasteful on prose or short on dense text - and
                // this corpus contains both. Rather than guess it, shrink and retry when the server says
                // the input is too large. Deterministic, bounded, and it keeps the full text on the
                // ordinary case instead of cutting everything to the worst case's budget.
                HttpResponseMessage response;
                var body = string.Empty;
                while (true)
                {
                    response = await http.PostAsJsonAsync($"{baseUrl}/v1/embeddings",
                        new { model, input }, ct);
                    body = await response.Content.ReadAsStringAsync(ct);
                    if (response.IsSuccessStatusCode || input.Length <= MinInputChars ||
                        !body.Contains("too large", StringComparison.OrdinalIgnoreCase)) break;

                    response.Dispose();
                    input = input[..Math.Max(MinInputChars, input.Length / 2)];
                    Interlocked.Increment(ref _truncated);
                }

                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }

                using var json = JsonDocument.Parse(body);
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

    /// <summary>What a bench arm needs of a chat model, so a strong CLI-driven one can stand in for the
    /// local HTTP one. It exists for the field-baseline arms: a write-time consolidation baseline is only
    /// worth comparing against if it was built to a good standard, and the 4B local model is not that.
    /// </summary>
    internal interface IBenchChat
    {
        /// <summary>What this instance asks, for a table to label its row with.</summary>
        string Model { get; }

        Task<string?> AskAsync(string prompt, CancellationToken ct = default, int maxTokens = 4);
    }

    /// <summary>A strong model reached through the `claude` CLI, one question per process.
    ///
    /// <para><b>It ignores <c>maxTokens</c></b>, and says so rather than accepting it silently: the CLI
    /// exposes no output cap, so an arm that depends on a tight cap must not use this chat. Every caller
    /// here wants a phrase or a list, which is why it is safe for them.</para>
    ///
    /// <para>Resolved from PATH by NAME, never by an absolute path — a machine path in a tracked file is
    /// what `.claude/rules/sensitive-info.md` forbids. <c>LYNTAI_BENCH_CLI</c> overrides for an install that
    /// is not on PATH.</para></summary>
    internal sealed class ClaudeCliChat(string exe) : IBenchChat
    {
        public string Model => $"{exe} (CLI)";

        public async Task<bool> ReachableAsync()
        {
            try { return await AskAsync("Reply with exactly: 1") is { Length: > 0 }; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public async Task<string?> AskAsync(string prompt, CancellationToken ct = default, int maxTokens = 4)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(prompt);

            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            // A non-zero exit with output is still an answer worth reading; an empty one never is.
            return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
        }
    }

    /// <summary>The CLI chat if it answers, else null with a reason — the same refuse-rather-than-substitute
    /// posture <see cref="TryRealChatAsync"/> takes, because a baseline that silently fell back to the 4B
    /// model would be measured as the strong one.</summary>
    internal static async Task<ClaudeCliChat?> TryCliChatAsync(string why)
    {
        var exe = Environment.GetEnvironmentVariable("LYNTAI_BENCH_CLI") ?? "claude";
        var chat = new ClaudeCliChat(exe);
        if (await chat.ReachableAsync().ConfigureAwait(false)) return chat;

        Console.Error.WriteLine($"{why}: the `{exe}` CLI did not answer, and this arm will not substitute a");
        Console.Error.WriteLine("  weaker model for it — the whole point is a baseline built to a good");
        Console.Error.WriteLine("  standard. Install it, or point LYNTAI_BENCH_CLI at it.");
        return null;
    }

    /// <summary>A real chat model over the OpenAI-compatible route, asked one question at a time.</summary>
    internal sealed class OpenAiCompatibleChat(HttpClient http, string baseUrl, string model) : IBenchChat
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

        /// <remarks>Temperature 0, and a tiny cap by DEFAULT: every question the policy sweeps ask has a
        /// one-token answer, and a model that wants to explain itself is spending latency the seam cannot
        /// afford. <paramref name="maxTokens"/> raises it for a caller that genuinely needs a phrase — the
        /// LoCoMo reader wants a few words — and the default is unchanged so no existing arm moves.</remarks>
        public async Task<string?> AskAsync(string prompt, CancellationToken ct = default, int maxTokens = 4)
        {
            using var response = await http.PostAsJsonAsync($"{baseUrl}/v1/chat/completions",
                new
                {
                    model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0,
                    max_tokens = maxTokens,
                }, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();
        }
    }
}
