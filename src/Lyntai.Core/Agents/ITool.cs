namespace Lyntai.Agents;

/// <summary>
/// An executable tool the model can call inside an <see cref="IToolLoop"/>. Registered into DI as an
/// <see cref="IEnumerable{ITool}"/> keyed by <see cref="Name"/> (the DI-collection variation point —
/// adding a tool is a new class + one registration, never a switch). The declaration half
/// (<see cref="Name"/>/<see cref="Description"/>/<see cref="ParametersJsonSchema"/>) mirrors
/// <see cref="Lyntai.Llm.LlmTool"/>; <see cref="InvokeAsync"/> is the execution half.
/// </summary>
public interface ITool
{
    /// <summary>Stable name the model calls the tool by (e.g. "get_weather"). Unique per registry.</summary>
    string Name { get; }

    /// <summary>Human/model-readable description of what the tool does and when to use it.</summary>
    string? Description { get; }

    /// <summary>JSON-schema source describing the tool's arguments object (optional but recommended —
    /// it's what tells the model how to shape the arguments it passes to <see cref="InvokeAsync"/>).</summary>
    string? ParametersJsonSchema { get; }

    /// <summary>Run the tool. <paramref name="argumentsJson"/> is the arguments object the model
    /// produced (a JSON object, possibly empty); the return value is the observation fed back to the
    /// model (JSON or plain text). Throwing is tolerated — the loop turns a thrown message into an
    /// error observation so the model can recover — but honor <paramref name="ct"/>.</summary>
    Task<string> InvokeAsync(string argumentsJson, CancellationToken ct = default);
}
