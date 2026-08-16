namespace Lyntai.Llm;

/// <summary>
/// OPTIONAL capability of an <see cref="ILlmProvider"/> whose backend owns its own credentials and can
/// report whether it HAS them — a signed-in CLI, a gateway with a session, a service with a stored key.
/// It answers the one question every consumer needs settled before a turn can possibly succeed ("is this
/// backend authenticated, and as whom?") WITHOUT running a completion, and lets a host drive sign-in /
/// sign-out instead of telling the user to open a terminal.
///
/// The alternative a consumer is left with otherwise is running a completion and pattern-matching the
/// failure text — a wasted, possibly billed turn — or shelling out to the backend's CLI itself, which is
/// exactly the bespoke provider handling Lyntai exists to remove.
///
/// Discovered by pattern-matching (<c>provider is IProviderAuth a</c>) over the registered provider
/// collection, like <see cref="IProviderProbe"/> / <see cref="IProviderUpdater"/>: a backend with
/// no login story (an API-key provider configured by the host, a local GGUF runtime) simply doesn't
/// implement it.
///
/// Lyntai NEVER stores credentials — the backend owns its own. This seam only asks and drives.
/// </summary>
public interface IProviderAuth
{
    /// <summary>Report whether the backend is authenticated, WITHOUT running a completion (no tokens, no
    /// model call). "Not signed in" is a normal ANSWER, so it comes back as
    /// <see cref="ProviderAuthStatus.Authenticated"/> <c>false</c> — never a throw; implementations must
    /// also FAIL SAFE for an absent/unreachable/stalled backend (same shape, reason in
    /// <see cref="ProviderAuthStatus.Detail"/>), except for caller cancellation, which propagates.</summary>
    /// <remarks>Because both "signed out" and "backend broken" surface as <c>Authenticated: false</c>, a
    /// caller that must distinguish them should ask <see cref="IProviderProbe.ProbeAsync"/> first —
    /// that reports reachability.</remarks>
    Task<ProviderAuthStatus> StatusAsync(CancellationToken ct = default);

    /// <summary>Start the backend's own sign-in flow. INTERACTIVE by nature (it typically opens a browser),
    /// so the honest contract is "run it and report what happened", not "guarantee a signed-in state" —
    /// read <see cref="ProviderAuthResult.Status"/> for the state it actually left behind.</summary>
    /// <param name="request">Which account kind / identity hint to use; <c>null</c> means the backend's own
    /// default.</param>
    /// <param name="ct">Cancels the wait — a human who abandons the browser flow is the expected case.</param>
    /// <remarks>Implementations BLOCK until the flow completes, fails, or a bounded budget expires (they
    /// must never wait forever), and must FAIL SAFE rather than throw, except for cancellation. A UI can
    /// therefore show a spinner for the duration and does not need to poll
    /// <see cref="StatusAsync"/> afterwards.</remarks>
    Task<ProviderAuthResult> LoginAsync(ProviderLoginRequest? request = null, CancellationToken ct = default);

    /// <summary>Sign the backend out, and report the state it was left in. MUTATES stored credentials, so a
    /// host should gate it behind a user action. Fails safe rather than throwing, except for
    /// cancellation.</summary>
    Task<ProviderAuthResult> LogoutAsync(CancellationToken ct = default);
}

/// <summary>What a backend says about its own authentication.</summary>
/// <param name="Authenticated">The backend reports usable credentials. <c>false</c> covers BOTH "signed
/// out" and "couldn't be asked" — <see cref="Detail"/> says which, and
/// <see cref="IProviderProbe.ProbeAsync"/> distinguishes them properly.</param>
/// <param name="Method">How it is signed in, in the backend's OWN vocabulary (a subscription account, a
/// console/API-billed account, an SSO tenant, an API key). Free-form on purpose: another backend's account
/// kinds must fit without an enum change. Null when unknown.</param>
/// <param name="Account">Who it is signed in AS — an email, an org/tenant name, a key label. Null when the
/// backend doesn't say. Treat as user data: it identifies a person.</param>
/// <param name="Detail">What the backend reported, verbatim (a diagnostics pane), or the reason it couldn't
/// be asked.</param>
public sealed record ProviderAuthStatus(
    bool Authenticated,
    string? Method = null,
    string? Account = null,
    string? Detail = null);

/// <summary>Options for <see cref="IProviderAuth.LoginAsync"/>. Every field is optional: the default is
/// "sign in the way this backend normally does".</summary>
/// <param name="Mode">Which account kind to sign in as, in the backend's own vocabulary (e.g. a
/// subscription account vs. a console/API-billed one). Free-form on purpose — see
/// <see cref="ProviderAuthStatus.Method"/>. An implementation that doesn't recognize the value must REFUSE
/// (a failed result) rather than invent a backend flag from it. Null = the backend's default.</param>
/// <param name="Email">An identity hint to pre-fill in the sign-in flow, when the backend accepts one. Not
/// a credential.</param>
/// <param name="Sso">Ask for the backend's SSO flow, where it has one.</param>
public sealed record ProviderLoginRequest(
    string? Mode = null,
    string? Email = null,
    bool Sso = false);

/// <summary>The outcome of <see cref="IProviderAuth.LoginAsync"/> / <see cref="IProviderAuth.LogoutAsync"/>.
/// The two fields answer different questions, deliberately: <paramref name="Succeeded"/> is "did the
/// command run and report success", <paramref name="Status"/> is "what state is the backend in now" — the
/// authority on whether a turn will work.</summary>
/// <param name="Succeeded">The backend's own auth command completed successfully.</param>
/// <param name="Status">The state read back AFTER the command, when it could be read; null when the command
/// failed (nothing reliable to report).</param>
/// <param name="Detail">The backend's own output, or the failure reason. Surface it verbatim rather than
/// parsing it — the wording belongs to the backend and changes.</param>
public sealed record ProviderAuthResult(
    bool Succeeded,
    ProviderAuthStatus? Status = null,
    string? Detail = null);
