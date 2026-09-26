using Lyntai.Generation.Providers;
using Lyntai.Inference;
using Lyntai.Processes;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The local piper TTS backend — the platform's first STREAMING audio backend, and the reason
/// <see cref="IProcessRunner.StreamBytesAsync"/> exists: raw PCM is binary, and a line-shaped read would
/// treat 0x0A as framing. Driven through the BYO <see cref="IProcessRunner"/>, so no binary is spawned
/// here; <c>PiperLiveTests</c> is what runs the real engine.</summary>
public class PiperProviderTests : IDisposable
{
    private readonly List<ScratchDir> _scratch = [];

    public void Dispose() => _scratch.ForEach(s => s.Dispose());

    private (PiperProvider Provider, FakeProcessRunner Runner, string Dir) Provider(
        Action<PiperOptions>? configure = null, bool createFiles = true, string? voiceJson = null)
    {
        var scratch = new ScratchDir("piper");
        _scratch.Add(scratch);
        var dir = scratch.Path;
        var exe = scratch.Combine("piper.exe");
        var model = scratch.Combine("voice.onnx");
        if (createFiles)
        {
            scratch.File("piper.exe");
            scratch.File("voice.onnx");
            if (voiceJson is not null) scratch.File("voice.onnx.json", voiceJson);
        }

        var options = new PiperOptions { BinaryPath = exe, ModelPath = model };
        configure?.Invoke(options);
        var runner = new FakeProcessRunner { StreamBytes = [[1, 2, 3], [4, 5]] };
        return (new PiperProvider(options, runner), runner, dir);
    }

    private static MediaRequest Ask(string? prompt = "hello world") =>
        new() { Kind = ProviderKinds.Audio, Prompt = prompt };

    private static async Task<List<MediaChunk>> Collect(PiperProvider provider, MediaRequest request)
    {
        var chunks = new List<MediaChunk>();
        await foreach (var chunk in provider.StreamAsync(request)) chunks.Add(chunk);
        return chunks;
    }

    [Theory]
    [InlineData("""{"audio": {"sample_rate": "22050"}}""")]   // a hand-edited config: a string, not a number
    [InlineData("""{"audio": {"sample_rate": 0}}""")]
    [InlineData("""{"audio": {"sample_rate": -8000}}""")]
    [InlineData("""{"audio": {"sample_rate": 22050.5}}""")]
    public async Task A_sample_rate_that_is_not_a_positive_integer_is_unstated_rather_than_a_throw(string voiceJson)
    {
        // TryGetInt32 THROWS on a non-number, outside the JsonException catch, so relying on it yields no
        // chunks; and a rate of 0 types the chunks `rate=0` and divides the duration by zero
        var (provider, _, _) = Provider(voiceJson: voiceJson);

        var chunks = await Collect(provider, Ask());

        Assert.All(chunks.Where(c => c.Data is { Length: > 0 }),
            c => Assert.Equal(PiperProvider.PcmMediaType(null), c.MediaType));
        Assert.Null(chunks[^1].Usage?.Seconds);
    }

    [Fact]
    public void It_declares_a_local_streaming_audio_backend()
    {
        var (provider, _, _) = Provider();

        Assert.Equal("piper", provider.Id);
        Assert.Equal([ProviderKinds.Text], provider.Capabilities.Accepts);
        Assert.Equal([ProviderKinds.Audio], provider.Capabilities.Produces);
        Assert.Equal([ProviderOperation.Complete, ProviderOperation.Stream], provider.Capabilities.Operations);
    }

    [Fact]
    public async Task The_argv_is_voice_plus_raw_switch_and_the_TEXT_travels_over_stdin_never_argv()
    {
        var (provider, runner, dir) = Provider();

        await Collect(provider, Ask("the text to speak"));

        Assert.Equal(
            ["--model", Path.Combine(dir, "voice.onnx"), "--output-raw"],
            runner.LastArgs!.ToList());
        Assert.Equal("the text to speak", runner.LastStdin);
        Assert.DoesNotContain("the text to speak", runner.LastArgs!);
    }

    [Fact]
    public async Task Chunks_carry_pcm_with_the_rate_read_from_the_VOICES_own_config()
    {
        var (provider, _, _) = Provider(voiceJson: """{"audio": {"sample_rate": 22050}}""");

        var chunks = await Collect(provider, Ask());

        Assert.Equal(3, chunks.Count);   // two content, one terminal
        Assert.Equal([1, 2, 3], chunks[0].Data);
        Assert.Equal("audio/pcm;rate=22050;bits=16;channels=1;endian=little", chunks[0].MediaType);
        Assert.True(chunks[2].Final);
        Assert.Null(chunks[2].Error);
        // 5 bytes at 22050 Hz s16le mono — Seconds is DERIVED from what actually streamed
        Assert.Equal(5 / 44100.0, chunks[2].Usage!.Seconds!.Value, 9);
    }

    [Fact]
    public async Task An_explicit_SampleRate_outranks_the_voice_config_and_a_missing_config_states_no_rate()
    {
        var (explicitRate, _, _) = Provider(o => o.SampleRate = 16000,
            voiceJson: """{"audio": {"sample_rate": 22050}}""");
        Assert.Equal("audio/pcm;rate=16000;bits=16;channels=1;endian=little",
            (await Collect(explicitRate, Ask()))[0].MediaType);

        var (noConfig, _, _) = Provider();   // no voice json at all
        var chunks = await Collect(noConfig, Ask());
        Assert.Equal("audio/pcm;bits=16;channels=1;endian=little", chunks[0].MediaType);
        Assert.Null(chunks[^1].Usage!.Seconds);   // no rate → no derived duration, never a guess
    }

    [Fact]
    public async Task An_unprovisioned_engine_is_NOT_CONFIGURED_and_never_spawns()
    {
        var (provider, runner, _) = Provider(createFiles: false);

        var chunks = await Collect(provider, Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(ProviderVerdict.NotConfigured, Assert.Single(chunks).Error);
        Assert.False(probe.Available);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Speech_without_text_is_refused_before_the_spawn()
    {
        var (provider, runner, _) = Provider();

        var missing = await Collect(provider, Ask(prompt: null));
        var blank = await Collect(provider, Ask(prompt: "   "));

        Assert.Equal(ProviderVerdict.Unsupported, Assert.Single(missing).Error);
        Assert.Equal(ProviderVerdict.Unsupported, Assert.Single(blank).Error);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task A_timeout_and_a_failed_exit_map_to_verdicts_after_the_chunks_that_arrived()
    {
        var (timedOut, runner, _) = Provider();
        runner.ThrowsAfterBytes = new ProcessTimeoutException("piper", TimeSpan.FromSeconds(60));
        var chunks = await Collect(timedOut, Ask());
        Assert.Equal(3, chunks.Count);   // both content chunks arrived first, as the real runner yields
        Assert.Equal(ProviderVerdict.Timeout, chunks[2].Error);

        var (failed, runner2, _) = Provider();
        runner2.ThrowsAfterBytes = new ProcessRunException("piper", 1, "voice file is corrupt");
        var last = (await Collect(failed, Ask()))[^1];
        Assert.NotNull(last.Error);
        Assert.Contains("corrupt", last.Detail);
    }

    [Fact]
    public async Task A_runner_without_the_binary_member_reports_a_wiring_gap_not_an_engine_failure()
    {
        var (provider, runner, _) = Provider();
        runner.ThrowsAfterBytes = new NotSupportedException(
            "this IProcessRunner does not implement StreamBytesAsync");
        runner.StreamBytes = [];

        var chunk = Assert.Single(await Collect(provider, Ask()));

        Assert.Equal(ProviderVerdict.NotConfigured, chunk.Error);
        Assert.Contains("StreamBytesAsync", chunk.Detail);
    }

    [Fact]
    public async Task The_buffered_mode_is_the_SAME_stream_collected_so_the_two_cannot_disagree()
    {
        var (provider, _, _) = Provider(voiceJson: """{"audio": {"sample_rate": 22050}}""");

        var result = await provider.GenerateAsync(Ask());

        Assert.True(result.IsOk, result.Detail);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal([1, 2, 3, 4, 5], artifact.Data);
        Assert.Equal("audio/pcm;rate=22050;bits=16;channels=1;endian=little", artifact.MediaType);
        Assert.Equal(5 / 44100.0, result.Usage!.Seconds!.Value, 9);
    }

    [Fact]
    public async Task A_host_can_respell_a_flag_and_append_engine_specific_args_LAST()
    {
        var (provider, runner, dir) = Provider(o =>
        {
            o.ArgvFlags["output-raw"] = "--output_raw";
            o.ExtraArgs = ["--speaker", "3"];
        });

        await Collect(provider, Ask());

        Assert.Equal(
            ["--model", Path.Combine(dir, "voice.onnx"), "--output_raw", "--speaker", "3"],
            runner.LastArgs!.ToList());
    }

    [Fact]
    public async Task The_runner_gets_an_inactivity_clock_with_an_absolute_backstop_not_one_wall_clock()
    {
        var (provider, runner, _) = Provider(o => o.Timeout = TimeSpan.FromMinutes(30));

        await Collect(provider, Ask());

        Assert.Equal(TimeSpan.FromMinutes(30), runner.LastMaxDuration);
        Assert.True(runner.LastInactivityTimeout < runner.LastMaxDuration,
            "the inactivity window must be shorter than the absolute backstop");
    }
}
