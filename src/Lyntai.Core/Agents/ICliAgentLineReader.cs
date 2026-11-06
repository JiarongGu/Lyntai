namespace Lyntai.Agents;

/// <summary>Reads the lines a CLI agent program prints into the events of one turn: the one thing a host supplies to run its
/// own program under the library's turn rules (<c>CliAgentTurn</c> in <c>Lyntai.Providers.Basic</c>). The turn keeps what is
/// the same on every door: exactly one <see cref="SessionEnded"/> (the first any source yields), a process fault made the
/// terminal, and a failed turn's <see cref="AgentFailure.Delivery"/> stamped from what the turn showed after the prompt was
/// written. A reader holds only its program's own grammar, and one reader serves one turn.
/// <para><b>Implementer's contract.</b> Never throw: a line the reader cannot read yields no events, and a throw escapes
/// the turn. Yield <see cref="SessionEnded"/> from <see cref="Read"/> where the program prints its own ending, with
/// <see cref="SessionEnded.Failure"/> set on a failed one; a program that prints none is ended in
/// <see cref="ReadEnd"/>.</para></summary>
public interface ICliAgentLineReader
{
    /// <summary>Translates one line the program printed into zero or more events.</summary>
    /// <param name="line">One line of the program's standard output, without its line break.</param>
    IReadOnlyList<AgentStreamEvent> Read(string line);

    /// <summary>The program's output has ended: told once, after the last line, with the program's exit code (0 on a clean
    /// end, else the code the runner reported through <see cref="Lyntai.Processes.ProcessRunException.ExitCode"/>). The terminal it
    /// returns ends a turn that has none, so a program that prints no ending of its own is ended here; it is ignored when
    /// the turn already ended, and null leaves the ending to the turn: a clean end with no terminal is a failed turn, and a
    /// non-zero exit with none is the process fault. Not called for a stall, a cancellation or a program that would not
    /// start, whose terminal is the process fault's.</summary>
    /// <param name="exitCode">The exit code.</param>
    SessionEnded? ReadEnd(int exitCode);

    /// <summary>The frames the reader read nothing from, by type, asked once when a terminal is stamped
    /// (<see cref="SessionEnded.Unread"/>). Default: none.</summary>
    IReadOnlyList<AgentUnreadFrame> Unread() => [];
}
