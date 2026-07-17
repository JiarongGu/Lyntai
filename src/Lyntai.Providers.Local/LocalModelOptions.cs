namespace Lyntai.Providers.Local;

/// <summary>
/// Configuration for the in-process <see cref="LocalProvider"/>. The only required field is
/// <see cref="ModelPath"/> (a GGUF file on disk); the rest mirror the common llama.cpp knobs.
/// </summary>
public sealed class LocalModelOptions
{
    /// <summary>Absolute (or app-relative) path to the GGUF model file to load.</summary>
    public string ModelPath { get; set; } = "";

    /// <summary>Context window (tokens) for the loaded model. Null uses the model's own trained
    /// maximum from its GGUF metadata — the right default for most models.</summary>
    public uint? ContextSize { get; set; }

    /// <summary>Layers to offload to the GPU. 0 (default) is pure CPU; a large value (e.g. 999)
    /// offloads everything. Requires a GPU LLamaSharp.Backend.* in the consuming app.</summary>
    public int GpuLayerCount { get; set; }

    /// <summary>Default cap on generated tokens when a request does not set
    /// <see cref="Lyntai.Llm.LlmRequest.MaxTokens"/>. Null lets generation run to the model's EOS.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Default sampling temperature when a request does not set one.</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Extra stop strings that end generation early (beyond the model's own EOS token) —
    /// e.g. a role tag a base (non-chat) model would otherwise hallucinate.</summary>
    public IReadOnlyList<string> AntiPrompts { get; set; } = [];
}
