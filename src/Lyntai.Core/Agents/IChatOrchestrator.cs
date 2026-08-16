namespace Lyntai.Agents;

/// <summary>
/// Two-gate chat orchestration (design §9): composes the library's pieces into one guarded chat turn —
/// <b>input gate</b> (guards) → memory recall into the prompt → the model (via the tool loop, so it can
/// call tools) → <b>output gate</b> (guards) → remember the exchange. The two gates are the guard rail
/// applied before and after the model; everything else is fail-open. Inject this for a batteries-included
/// chat entry point, or keep composing the primitives yourself.
/// <para>NOTE: the gates are the ENTRY and FINAL-ANSWER of the turn. Tool content has its OWN gates inside
/// the loop: every tool call's arguments are inspected before it runs
/// (<see cref="Lyntai.Guards.IGuardRail.InspectToolCallAsync"/>) and every observation before it is fed back
/// (<see cref="Lyntai.Guards.IGuardRail.InspectToolResultAsync"/>). What is NOT individually gated is the
/// loop's intermediate MODEL turns — the assistant messages between tool round-trips. To gate every model
/// turn, layer <c>GuardedLlmClient</c> onto the front door with
/// <c>AddFrontDoorDecorator(order, (sp, inner) =&gt; new GuardedLlmClient(inner, rail))</c>; don't ALSO rely
/// on these gates for that content, to avoid double-gating.
/// <para><b>Do NOT register your own <see cref="Lyntai.Llm.ILlmClient"/> to achieve this</b>, which is what
/// this paragraph advised until 2026-08-16. A pre-registered client THROWS when any front-door decorator is
/// configured — <c>AddLyntai</c> refuses the contradiction rather than let governance vanish — and when none
/// is, it silently loses refusal screening, which is applied inside the front-door fold rather than through
/// the decorator list.</para></para>
/// </summary>
public interface IChatOrchestrator
{
    Task<ChatResult> ChatAsync(ChatTurn turn, CancellationToken ct = default);
}
