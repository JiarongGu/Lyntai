using System.Text.Json;
using Lyntai.Agents;

namespace Lyntai.Providers.Acp;

/// <summary>What one ACP agent says in the places the protocol leaves to its agents — an update's <c>_meta</c>, an
/// error's <c>data</c>, the prompt answer's <c>_meta</c> — read as the neutral vocabulary. The door reads the
/// protocol's own fields itself; a reader is how it learns an agent's extensions, so a third agent is taught by
/// registering one with <c>AddAcpAgentSession</c> and never by a fork. The shipped readers are in
/// <see cref="AcpExtensionReaders"/>.
/// <para>Every member has an answer that says nothing, so a reader implements the ones its agent uses. A reader
/// is called from the turn's one reading loop for every frame of every turn of its door, so it is stateless and
/// thread-safe, and it does not throw: a member that throws a JSON shape fault loses its own reading and nothing
/// else.</para></summary>
public interface IAcpExtensionReader
{
    /// <summary>Read one <c>session/update</c> of the prompt, whatever its <c>sessionUpdate</c>.</summary>
    /// <param name="update">The update object, <c>sessionUpdate</c> included.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <returns>Null when the update says nothing this reader knows, which leaves whatever this reader held from an
    /// earlier update as it was.</returns>
    AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time) => null;

    /// <summary>Read a JSON-RPC error the agent answered a request with, for the failure it states as a field.</summary>
    /// <param name="error">The response's <c>error</c> object: <c>code</c>, <c>message</c> and <c>data</c>. The
    /// door reads the code ACP defines for a sign-in itself.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <returns>The failure the error's fields state, or null. The agent's prose is the door's to read and is not
    /// this member's.</returns>
    AgentFailure? ReadError(JsonElement error, TimeProvider time) => null;

    /// <summary>Read the answer to <c>session/prompt</c> for a failure the agent states in a turn it ended without
    /// an error (a <c>stopReason</c> of <c>end_turn</c> that failed).</summary>
    /// <param name="answer">The answer's <c>result</c> object.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <returns>Null when the turn did not fail by this reader's reading, so the turn ends by its stop reason.</returns>
    AcpStatedFailure? ReadTurnEnd(JsonElement answer, TimeProvider time) => null;
}

/// <summary>What one reader took from one update.</summary>
/// <param name="Events">Events to yield now, after the ones the door read from the same update. A
/// <see cref="SessionStarted"/> or <see cref="SessionEnded"/> is dropped: a session starts when the door opens it
/// and ends once, by the door.</param>
/// <param name="Failure">A failure the update states, held until the turn fails and read with the failure the end
/// states. A reading REPLACES what the same reader held before, so null here clears it: the way an agent says a
/// problem was recovered from. A reader that says nothing returns null for the whole reading instead, which clears
/// nothing.</param>
public sealed record AcpUpdateReading(IReadOnlyList<AgentStreamEvent>? Events = null, AgentFailure? Failure = null)
{
    /// <summary>True when this reading says nothing of the failure it holds, so what the same reader held before stands
    /// rather than being replaced by <c>Failure</c>: events alone.</summary>
    public bool KeepsFailure { get; init; }
}

/// <summary>A failure an agent stated in the answer to a prompt it ended normally.</summary>
/// <param name="By">What stated it, for <see cref="AgentFailure.By"/> when neither <paramref name="Failure"/> nor the
/// words decide the kind, which is then <see cref="AgentFailureKind.Fatal"/>.</param>
/// <param name="Words">The agent's words for it: the terminal's diagnostic, and a sentence that may fill what
/// <paramref name="Failure"/> left open (it never overrides it). Null when the agent gave none.</param>
/// <param name="Failure">What the agent's field says, or null when the field names no kind.</param>
public sealed record AcpStatedFailure(string By, string? Words = null, AgentFailure? Failure = null);
