namespace Lyntai.Inference;

/// <summary>A tool the model may call. <paramref name="ParametersJsonSchema"/> is the JSON-schema
/// source for the tool's arguments; providers translate it to their native tool format.</summary>
public sealed record TextTool(string Name, string? Description = null, string? ParametersJsonSchema = null);
