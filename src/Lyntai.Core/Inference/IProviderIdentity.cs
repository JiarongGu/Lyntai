namespace Lyntai.Inference;

/// <summary>The one thing every pluggable backend has: a stable id the router matches candidates against.
///
/// <para>It exists so lifetime machinery — <c>IProviderPool&lt;TProvider&gt;</c> above all — can be written
/// once for every provider seam instead of once per domain. The two domain seams it was extracted from both
/// already declared exactly this member, so adopting it as a base changed no implementation anywhere — and
/// they have since collapsed into the single <see cref="Lyntai.Inference.IModelProvider"/> (D127).</para>
///
/// <para><b><see cref="Lyntai.Inference.IModelProvider"/> still declares <c>Id</c> itself</b> (as
/// <c>new</c>), and must keep doing so. Adding
/// a base interface is binary-compatible; removing the member from the DERIVED interface is not — a
/// pre-compiled caller emits <c>callvirt IModelProvider::get_Id</c>, and member resolution does not walk base
/// interfaces, so the call would throw <see cref="MissingMethodException"/> against a rebuilt Lyntai until
/// the consumer recompiled. Recompiling is exactly what a package upgrade does not do for an assembly the
/// consumer did not rebuild, and what <c>consumer-smoke</c> cannot catch, because it recompiles everything.
/// The redundant-looking declarations are the compatibility.</para></summary>
public interface IProviderIdentity
{
    /// <summary>The candidate id routing selects on (<c>"claude-cli"</c>, <c>"a1111"</c>,
    /// <c>"local-diffusion"</c>). Stable across the process lifetime.</summary>
    string Id { get; }
}
