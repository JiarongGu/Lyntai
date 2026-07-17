namespace Lyntai.Agents;

/// <summary>
/// Two-gate chat orchestration (design §9): composes the library's pieces into one guarded chat turn —
/// <b>input gate</b> (guards) → memory recall into the prompt → the model (via the tool loop, so it can
/// call tools) → <b>output gate</b> (guards) → remember the exchange. The two gates are the guard rail
/// applied before and after the model; everything else is fail-open. Inject this for a batteries-included
/// chat entry point, or keep composing the primitives yourself.
/// </summary>
public interface IChatOrchestrator
{
    Task<ChatResult> ChatAsync(ChatTurn turn, CancellationToken ct = default);
}
