namespace Lyntai.Llm;

/// <summary>A canonical completion request; providers translate it to their native schema.</summary>
public sealed record LlmRequest
{
    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    /// <summary>Model id; a provider resolves null to its own default.</summary>
    public string? Model { get; init; }

    public int? MaxTokens { get; init; }

    public double? Temperature { get; init; }

    /// <summary>Structured output: a JSON schema the reply must conform to (optional).</summary>
    public string? JsonSchema { get; init; }

    public IReadOnlyList<LlmTool>? Tools { get; init; }

    /// <summary>Per-feature routing/telemetry tag (e.g. "scoring", "chat").</summary>
    public string Consumer { get; init; } = "default";
}
