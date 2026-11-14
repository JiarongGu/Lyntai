using System.Diagnostics;

namespace Lyntai.Tests.Fakes;

/// <summary>Liveness and clean-up by pid for a process a test's stand-in started, one copy for every file that asks.</summary>
public static class ChildProcesses
{
    /// <summary>Whether <paramref name="pid"/> is a running process. An exited one waiting to be collected is not.</summary>
    public static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited && !Zombie(pid); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // A zombie has exited and waits only for its parent to collect it. A killed grandchild is reparented to PID 1, and
    // in a container whose PID 1 never reaps it stays one, which HasExited (does the pid exist?) reads as running.
    private static bool Zombie(int pid)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..] is ['Z' or 'X', ..];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException) { return false; }
    }

    /// <summary>Ends <paramref name="pid"/> and what it started, if it still runs; a process already gone is not an error.</summary>
    public static void End(int pid)
    {
        if (!Alive(pid)) return;
        try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); } catch { /* gone */ }
    }
}
