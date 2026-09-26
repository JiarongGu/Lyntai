using System.Text;
using System.Text.Json;
using Lyntai.Providers.Model2Vec;
using Lyntai.Inference;
using Microsoft.Extensions.DependencyInjection;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.VectorMath;

namespace Lyntai.Tests.Vectors;

/// <summary>The in-process, server-free vector backend.
///
/// <para>Every test here builds its OWN model — a real safetensors file and a real WordPiece vocabulary,
/// written to a temp directory — so the suite needs no download and no server. That is also what makes the
/// arithmetic checkable: with a table whose rows are known, a mean-pooled vector has one right answer.</para>
/// </summary>
public class Model2VecProviderTests : IDisposable
{
    private readonly ScratchDir _scratch = new("static");

    private string Dir => _scratch.Path;

    public void Dispose() => _scratch.Dispose();

    /// <summary>Writes a model whose row <c>i</c> is all-<c>i</c>, so a mean over known ids is exact.</summary>
    private string WriteModel(IReadOnlyList<string> vocabulary, int dimensions = 4, bool? normalize = false)
    {
        var rows = vocabulary.Count;
        var data = new float[rows * dimensions];
        for (var r = 0; r < rows; r++)
            for (var d = 0; d < dimensions; d++)
                data[(r * dimensions) + d] = r;

        var payload = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, payload, 0, payload.Length);

        var header = Encoding.UTF8.GetBytes(
            "{\"embeddings\":{\"dtype\":\"F32\",\"shape\":[" + rows + "," + dimensions
            + "],\"data_offsets\":[0," + payload.Length + "]}}");

        using (var file = File.Create(Path.Combine(Dir, "model.safetensors")))
        {
            file.Write(BitConverter.GetBytes((long)header.Length));
            file.Write(header);
            file.Write(payload);
        }

        File.WriteAllLines(Path.Combine(Dir, "vocab.txt"), vocabulary);
        if (normalize is { } n)
            File.WriteAllText(Path.Combine(Dir, "config.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["normalize"] = n }));
        return Dir;
    }

    /// <summary>A BERT vocabulary needs its special tokens present, and WordPiece needs [UNK].</summary>
    private static List<string> Vocabulary(params string[] words) =>
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", .. words];

    [Fact]
    public void Declares_EMBED_and_nothing_else_so_a_router_never_sends_it_a_chat()
    {
        // A vector backend is a provider like any other (D128) — what makes it a vector backend is the DECLARATION,
        // not a separate interface. Asserting the absences is the half that matters: it is what stops the
        // router dispatching a completion here and getting the default Unsupported back.
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha")));

        Assert.Equal([ProviderKinds.Text], vectorProvider.Capabilities.Accepts);
        Assert.Equal([ProviderKinds.Vector], vectorProvider.Capabilities.Produces);
        Assert.Equal([ProviderOperation.Complete], vectorProvider.Capabilities.Operations);
        Assert.Equal("model2vec", vectorProvider.Id);
        Assert.True(vectorProvider.IsAvailable);
    }

    [Fact]
    public void AddModel2Vec_registers_it_as_a_PROVIDER_as_well_as_the_vector_backend_slot()
    {
        // Both halves are load-bearing: the slot serves the one-vector-backend deployment, and
        // the provider collection is what lets a second vector backend be registered and told apart by id.
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddModel2VecProvider(WriteModel(Vocabulary("alpha"))));
        var provider = services.BuildServiceProvider();

        Assert.True(EmbeddingRouting.CanEmbed(provider.GetServices<IModelProvider>()));
        var asProvider = Assert.Single(provider.GetServices<IModelProvider>(), p => p.Id == "model2vec");
        Assert.Contains(ProviderKinds.Vector, asProvider.Capabilities.Produces);
    }

    [Fact]
    public void Reads_the_table_and_reports_the_models_own_width()
    {
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta"), dimensions: 7));

        Assert.Equal(7, vectorProvider.Dimensions);
    }

    [Fact]
    public async Task Mean_pools_the_rows_its_tokens_select()
    {
        // Rows are all-i, so "alpha beta" (ids 5 and 6) means exactly (5 + 6) / 2 = 5.5 in every dimension.
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));

        var vector = (await vectorProvider.EmbedAsync(["alpha beta"]))[0];

        Assert.All(vector, v => Assert.Equal(5.5f, v, 3));
    }

    [Fact]
    public async Task Pools_CONTENT_tokens_only_no_CLS_or_SEP_which_would_move_every_vector()
    {
        // PINNED because it is invisible if it changes. A model2vec table is built against the ids its own
        // pruned vocabulary produces; bracketing the input with [CLS]/[SEP] would fold two more rows into
        // every mean and shift each vector by an amount no test asserting "a vector came back" could see.
        // "alpha" alone must therefore be row 5 and nothing else.
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));

        var vector = (await vectorProvider.EmbedAsync(["alpha"]))[0];

        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task Normalizes_to_unit_length_when_the_model_says_so_and_not_when_it_does_not()
    {
        var normalized = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha"), normalize: true));
        var raw = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha"), normalize: false));

        var unit = (await normalized.EmbedAsync(["alpha"]))[0];
        Assert.Equal(1.0, Math.Sqrt(unit.Sum(v => (double)v * v)), 3);

        var plain = (await raw.EmbedAsync(["alpha"]))[0];
        Assert.True(Math.Sqrt(plain.Sum(v => (double)v * v)) > 1.5);
    }

    [Fact]
    public async Task An_EMPTY_text_yields_a_zero_vector_rather_than_throwing()
    {
        // A store full of documents must not be refused over one empty one, and a zero vector is what an
        // empty string MEANS — maximally dissimilar to everything.
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha")));

        var vectors = await vectorProvider.EmbedAsync(["", "   "]);

        Assert.All(vectors, v => Assert.All(v, component => Assert.Equal(0f, component)));
    }

    [Fact]
    public void A_MISSING_file_names_the_one_that_is_missing_rather_than_null_referencing()
    {
        // A partial download is the common failure and its unguarded symptom is a null reference far away.
        WriteModel(Vocabulary("alpha"));
        File.Delete(Path.Combine(Dir, "vocab.txt"));

        var error = Assert.Throws<FileNotFoundException>(() => Model2VecProvider.FromDirectory(Dir));
        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_safetensors_FAILS_rather_than_producing_plausible_vectors()
    {
        // The dangerous direction: a silently wrong table embeds fine and costs retrieval quality nobody
        // can trace back to it.
        WriteModel(Vocabulary("alpha"));
        File.WriteAllText(Path.Combine(Dir, "model.safetensors"), "this is not a tensor file at all");

        Assert.Throws<InvalidDataException>(() => Model2VecProvider.FromDirectory(Dir));
    }

    [Fact]
    public async Task Has_NO_context_limit_which_is_the_one_thing_this_class_has_over_a_small_transformer()
    {
        // Every sub-100 MB transformer vector backend rejects an input past 512 tokens. A lookup table has no
        // positional embeddings, so a long document is just more rows to average.
        // 4000 alphas THEN 4000 betas: the whole text means (5 + 6) / 2 = 5.5, while any truncation keeps only
        // alphas and means 5 — a periodic text would average the same either way
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));
        var text = string.Join(" ", [.. Enumerable.Repeat("alpha", 4000), .. Enumerable.Repeat("beta", 4000)]);

        var vector = (await vectorProvider.EmbedAsync([text]))[0];

        Assert.All(vector, v => Assert.Equal(5.5f, v, 3));
    }
}

/// <summary>The static vector backend against a REAL downloaded model, which the synthetic fixture cannot
/// stand in for: a real export ships a PRUNED vocabulary, so this is what would catch a table and a
/// tokenizer that disagree about which row an id names — the failure that produces plausible vectors and
/// an untraceable retrieval loss.
/// <para>Skipped without <c>LYNTAI_STATIC_MODEL_DIR</c>. Point it at a `potion-*` or
/// `static-retrieval-*` directory.</para></summary>
public class Model2VecProviderLiveTests
{
    private static string? Directory => Environment.GetEnvironmentVariable("LYNTAI_STATIC_MODEL_DIR");

    [SkippableFact]
    public async Task A_real_model_embeds_and_ranks_a_related_pair_above_an_unrelated_one()
    {
        Skip.If(string.IsNullOrWhiteSpace(Directory), "set LYNTAI_STATIC_MODEL_DIR to a model2vec directory");

        var vectorProvider = Model2VecProvider.FromDirectory(Directory!);
        var vectors = await vectorProvider.EmbedAsync([
            "the weather forecast for tomorrow",
            "a stock market share price quote",
            "tomorrow's weather forecast",
        ]);

        Assert.True(vectorProvider.Dimensions > 0);

        // The table's own vocabulary is PRUNED, so a wrong row mapping would still produce finite vectors.
        // Ordering a related pair above an unrelated one is the cheapest check that cannot pass on one.
        var related = Cosine(vectors[0], vectors[2]);
        var unrelated = Cosine(vectors[0], vectors[1]);
        Assert.True(related > unrelated,
            $"related {related:F4} should outrank unrelated {unrelated:F4} — a wrong row mapping looks like this");
    }

}
