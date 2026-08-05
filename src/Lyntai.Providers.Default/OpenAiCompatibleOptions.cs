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

    /// <summary>Context-window override for <see cref="OpenAiFlavor.Ollama"/> ONLY — it becomes Ollama's
    /// <c>options.num_ctx</c> on the native <c>/api/chat</c> payload. **Every other flavor IGNORES it
    /// silently**: the OpenAI-shaped payload has no equivalent knob (the context window is a property of
    /// the deployed model there), and that includes Ollama's own OpenAI-COMPATIBLE <c>/v1</c> surface,
    /// which resolves to <see cref="OpenAiFlavor.OpenAi"/>. The name carries the backend for exactly that
    /// reason — a generic one read as a portable setting and was not one.</summary>
    public int? OllamaContextSize { get; set; }
}
