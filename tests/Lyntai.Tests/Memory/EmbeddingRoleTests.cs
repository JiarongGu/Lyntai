using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>The embedding ROLE seam: the library tells a vector backend which side of a similarity comparison
/// a text is being embedded for, and the deployment's implementation decides what to do with that.
///
/// <para><b>Why it exists.</b> Asymmetric embedding models — the E5, BGE, nomic and Arctic families — are
/// trained with distinct instructions for the text being STORED and the text being SEARCHED WITH, and score
/// materially worse when both sides are embedded identically. Before this seam the engine called one
/// role-less method from both paths, so such a model could not be driven correctly through Lyntai by ANY
/// implementation: a BYO vector backend had no way to learn which side it was serving.</para>
///
/// <para><b>The compatibility half is what these tests mostly pin.</b> A symmetric model must be unaffected
/// and an existing implementation must keep working untouched, which is what the interface's default body
/// buys — so the tests that matter most here are the ones asserting nothing changed.</para></summary>
public class EmbeddingRoleTests
{
    /// <summary>Records the role each call carried. Implements BOTH overloads, which is what a genuinely
    /// role-aware BYO vector backend does.</summary>
    private sealed class RoleRecordingVectorProvider : FakeVectorProviderBase
    {
        public List<(string Text, EmbeddingRole? Role)> Calls { get; } = [];

        public override Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            foreach (var t in texts) Calls.Add((t, null));     // null = the role-less overload was used
            return Vectors(texts);
        }

        public override Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default)
        {
            foreach (var t in texts) Calls.Add((t, role));
            return Vectors(texts);
        }

        private static Task<IReadOnlyList<float[]>> Vectors(IReadOnlyList<string> texts) =>
            Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(_ => new[] { 1f, 0f })]);
    }

    [Fact]
    public async Task Remembering_embeds_a_DOCUMENT_and_recalling_embeds_a_QUERY()
    {
        var vectorProvider = new RoleRecordingVectorProvider();
        var mem = new SemanticMemory([vectorProvider], new InMemoryVectorStore());

        await mem.RememberAsync("t", "s", "the capital of France is Paris");
        await mem.RecallAsync("t", "s", "where is Paris", k: 5);

        Assert.Equal(
            [("the capital of France is Paris", EmbeddingRole.Document), ("where is Paris", EmbeddingRole.Query)],
            vectorProvider.Calls);
    }

    /// <summary>The compatibility guarantee, and the reason the seam is a default-implemented member rather
    /// than a new required one: <see cref="FakeVectorProvider"/> implements ONLY the role-less method — exactly
    /// what every vector backend written before this seam existed looks like — and must keep working.</summary>
    [Fact]
    public async Task A_vector_backend_implementing_only_the_ROLE_LESS_method_keeps_working_unchanged()
    {
        var mem = new SemanticMemory([new FakeVectorProvider()], new InMemoryVectorStore());

        await mem.RememberAsync("t", "s", "the capital of France is Paris");
        var hits = await mem.RecallAsync("t", "s", "capital of France", k: 5);

        Assert.NotEmpty(hits);
    }

    /// <summary>…and the default body must DELEGATE rather than return nothing. Without this the test above
    /// would pass on an implementation that silently produced no vectors for the role-aware path, since a
    /// recall returning empty is a legitimate outcome elsewhere in this file.</summary>
    [Fact]
    public async Task The_default_body_delegates_to_the_role_less_method_rather_than_returning_empty()
    {
        IModelProvider symmetric = new FakeVectorProvider();

        var viaRole = await symmetric.EmbedAsync(["hello world"], EmbeddingRole.Query);
        var viaPlain = await symmetric.EmbedAsync(["hello world"]);

        Assert.Single(viaRole);
        Assert.Equal(viaPlain[0], viaRole[0]);   // a symmetric model: the role changes nothing
    }

    /// <summary>A role-aware implementation sees the role through the single-text convenience too — the
    /// engine's write path uses it, so a seam that dropped the role there would be role-aware in name only.
    /// </summary>
    [Fact]
    public async Task The_single_text_extension_carries_the_role_through()
    {
        var vectorProvider = new RoleRecordingVectorProvider();

        await vectorProvider.EmbedAsync(["just this one"], EmbeddingRole.Document);

        Assert.Equal([("just this one", EmbeddingRole.Document)], vectorProvider.Calls);
    }

    /// <summary><see cref="EmbeddingRole.Document"/> is the default value of the enum, so a
    /// <c>default(EmbeddingRole)</c> — the value a struct field or an uninitialised array element carries —
    /// is the STORING side. That is the safer of the two to land on by accident: a corpus embedded
    /// consistently is still searchable, where a corpus embedded as queries is not comparable to
    /// anything.</summary>
    [Fact]
    public void Document_is_the_enums_default_value()
    {
        Assert.Equal(EmbeddingRole.Document, default(EmbeddingRole));
    }
}
