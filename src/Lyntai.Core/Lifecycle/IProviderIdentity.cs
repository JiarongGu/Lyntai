namespace Lyntai.Lifecycle;

/// <summary>The one thing every pluggable backend has: a stable id the router matches candidates against.
///
/// <para>It exists so lifetime machinery — <c>IProviderPool&lt;TProvider&gt;</c> above all — can be written
/// once for every provider seam instead of once per domain. <see cref="Lyntai.Llm.ILlmProvider"/> and
/// <see cref="Lyntai.Generation.IGenerationProvider"/> both already declared exactly this member, so adopting
/// it as a base changed no implementation anywhere.</para></summary>
public interface IProviderIdentity
{
    /// <summary>The candidate id routing selects on (<c>"claude-cli"</c>, <c>"a1111"</c>,
    /// <c>"local-diffusion"</c>). Stable across the process lifetime.</summary>
    string Id { get; }
}
