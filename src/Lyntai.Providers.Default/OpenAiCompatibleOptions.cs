namespace Lyntai.Providers.OpenAiCompatible;

public sealed class OpenAiCompatibleOptions
{
    /// <summary>Endpoint base, e.g. <c>https://api.openai.com</c>, <c>http://localhost:11434</c>,
    /// <c>https://openrouter.ai/api/v1</c>. The flavor is detected from this URL unless pinned.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>Bearer token; null for keyless endpoints (local Ollama).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model used when neither the request nor the candidate pins one.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>Pin the payload flavor; <see cref="OpenAiFlavor.Auto"/> (default) detects it from BaseUrl.</summary>
    public OpenAiFlavor Flavor { get; set; } = OpenAiFlavor.Auto;

    /// <summary>Ollama context-window override (maps to Ollama's <c>options.num_ctx</c> wire option;
    /// ignored by the other flavors).</summary>
    public int? ContextSize { get; set; }
}
