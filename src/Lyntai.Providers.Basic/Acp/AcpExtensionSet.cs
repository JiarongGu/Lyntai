using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.Acp;

/// <summary>One turn's readers and what each has held from the prompt's updates. A reader's JSON shape fault loses
/// its own reading and nothing of the door's.</summary>
internal sealed class AcpExtensionSet
{
    private readonly IReadOnlyList<IAcpExtensionReader> _readers;
    private readonly TimeProvider _time;
    private readonly AgentFailure?[] _held;

    public AcpExtensionSet(IReadOnlyList<IAcpExtensionReader> readers, TimeProvider time)
    {
        _readers = readers;
        _time = time;
        _held = new AgentFailure?[readers.Count];
    }

    /// <summary>What the readers hold from the updates so far.</summary>
    public IEnumerable<AgentFailure> Held => _held.OfType<AgentFailure>();

    public void ReadUpdate(JsonElement update, List<AgentStreamEvent> events)
    {
        for (var i = 0; i < _readers.Count; i++)
        {
            if (Guard(() => _readers[i].ReadUpdate(update, _time)) is not { } reading) continue;
            _held[i] = reading.Failure;
            if (reading.Events is null) continue;
            events.AddRange(reading.Events.Where(e => e is not (SessionStarted or SessionEnded)));
        }
    }

    public IEnumerable<AgentFailure> ReadError(JsonElement error) =>
        _readers.Select(r => Guard(() => r.ReadError(error, _time))).OfType<AgentFailure>().ToList();

    public IReadOnlyList<AcpStatedFailure> ReadTurnEnd(JsonElement answer) =>
        _readers.Select(r => Guard(() => r.ReadTurnEnd(answer, _time))).OfType<AcpStatedFailure>().ToList();

    private static T? Guard<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return null;
        }
    }
}
