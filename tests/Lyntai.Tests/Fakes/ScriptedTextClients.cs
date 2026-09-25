using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>The <see cref="ITextClient"/> doubles a model-backed memory policy is tested against — a scripted
/// reply, a broken backend, a caller's cancel and the backend's OWN timeout — shared by the annotation and
/// verification suites so the four failure shapes are spelled once. Unlike <see cref="FakeTextClient"/>,
/// <see cref="ScriptedTextClient"/> returns the SAME reply on every call.</summary>
internal abstract class TextClientDouble : ITextClient
{
    public abstract Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default);

    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<ProviderCapabilities?> GetCapabilitiesAsync(TextRequest req, CancellationToken ct = default) =>
        ValueTask.FromResult<ProviderCapabilities?>(null);
}

/// <summary>Answers every call with <paramref name="text"/> under <paramref name="verdict"/>, recording the
/// last request so a test can assert on what was ASKED, which no reply-scripted assertion can see.</summary>
internal sealed class ScriptedTextClient(string text, ProviderVerdict verdict = ProviderVerdict.Ok) : TextClientDouble
{
    public TextRequest? Last { get; private set; }

    public override Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        Last = req;
        return Task.FromResult(new TextResponse(text, verdict));
    }
}

/// <summary>A backend that cannot be reached.</summary>
internal sealed class ThrowingTextClient : TextClientDouble
{
    public override Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        throw new HttpRequestException("the backend is unreachable");
}

/// <summary>Honours the token, which <see cref="FakeTextClient"/> deliberately does not — a cancellation fact
/// is about the POLICY's catch ordering, and a client that ignored the token would make it pass vacuously.
/// Otherwise answers <paramref name="reply"/>.</summary>
internal sealed class TokenHonouringTextClient(string reply) : TextClientDouble
{
    public override Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new TextResponse(reply, ProviderVerdict.Ok));
    }
}

/// <summary>Throws exactly what <c>HttpClient</c> throws when its own timeout elapses — a cancellation nobody
/// asked for — while the caller's token stays uncancelled. That makes it a MODEL failure rather than a
/// cancel, and the pair with <see cref="TokenHonouringTextClient"/> is what stops the fix for one being
/// "swallow every cancellation".</summary>
internal sealed class TimingOutTextClient : TextClientDouble
{
    public override Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 300 seconds elapsing.");
}

/// <summary>A factory that hands out one client under every name.</summary>
internal sealed class SingleTextClientFactory(ITextClient client) : ITextClientFactory
{
    public ITextClient Get(string name) => client;
    public ITextClient Get() => client;
    public bool TryGet(string name, out ITextClient c) { c = client; return true; }
    public IReadOnlyList<string> Names => [];
}
