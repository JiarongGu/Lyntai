using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;

namespace Lyntai.Tests.Generation;

/// <summary>The piper backend against a REAL engine — the measurement this backend needs: whether
/// data-then-terminal is the decomposition a real TTS stream wants, which no fake can answer. The claim
/// under test is STREAMING itself — a real synthesis must arrive as several chunks before the terminal,
/// because one buffered blob would mean the "stream" door is a courtesy wrapper over a wait.
///
/// <para>Skipped without <c>LYNTAI_PIPER_CLI</c> (the piper executable) and <c>LYNTAI_PIPER_MODEL</c> (a
/// voice <c>.onnx</c> with its <c>.json</c> beside it). A synthesis is seconds, not minutes.</para></summary>
public class PiperLiveTests
{
    private static string? Binary => Environment.GetEnvironmentVariable("LYNTAI_PIPER_CLI");
    private static string? Voice => Environment.GetEnvironmentVariable("LYNTAI_PIPER_MODEL");

    [SkippableFact]
    public async Task A_real_synthesis_STREAMS_as_pcm_chunks_before_one_terminal_with_derived_duration()
    {
        Skip.If(string.IsNullOrWhiteSpace(Binary) || string.IsNullOrWhiteSpace(Voice),
            "set LYNTAI_PIPER_CLI to a real piper executable and LYNTAI_PIPER_MODEL to a voice .onnx");

        // low and x_low voices declare 16 kHz, so the rate is read off the voice rather than assumed
        using var voice = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Voice + ".json"));
        var rate = voice.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32();

        var provider = new PiperProvider(
            new PiperOptions { BinaryPath = Binary, ModelPath = Voice },
            new ProcessRunner());

        var chunks = new List<MediaChunk>();
        await foreach (var chunk in provider.StreamAsync(new MediaRequest
        {
            Kind = ProviderKinds.Audio,
            Prompt = "The numinous platform speaks entirely on this machine, with no key and no network. "
                + "A second sentence gives the engine something to stream across.",
        }))
            chunks.Add(chunk);

        Assert.All(chunks, c => Assert.Null(c.Error));
        var content = chunks.Where(c => c.Data is { Length: > 0 }).ToList();
        var terminal = Assert.Single(chunks, c => c.Final);

        // the STREAMING claim: several chunks before the terminal, not one buffered blob
        Assert.True(content.Count >= 2,
            $"a real synthesis should stream — it arrived as {content.Count} chunk(s)");

        // s16le at the voice's own declared rate: whole samples, typed precisely, seconds derived
        var totalBytes = content.Sum(c => (long)c.Data!.Length);
        Assert.Equal(0, totalBytes % 2);
        Assert.All(content, c =>
            Assert.Equal($"audio/pcm;rate={rate};bits=16;channels=1;endian=little", c.MediaType));
        var seconds = terminal.Usage!.Seconds!.Value;
        Assert.Equal(totalBytes / (rate * 2.0), seconds, 6);
        Assert.InRange(seconds, 1, 60);   // two spoken sentences are seconds of audio, not millis or minutes
    }
}
