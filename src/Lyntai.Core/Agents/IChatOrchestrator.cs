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
/// loop's intermediate MODEL turns — the assistant messages between tool round-trips. If you need every
/// model turn gated, register your <see cref="Lyntai.Llm.ILlmClient"/> as a <c>GuardedLlmClient</c> (the loop
/// then gates each turn); don't ALSO rely on these gates for that content, to avoid double-gating.</para>
/// </summary>
public interface IChatOrchestrator
{
    Task<ChatResult> ChatAsync(ChatTurn turn, CancellationToken ct = default);
}
