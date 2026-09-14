using System.Text;
using System.Text.Json;
using Lyntai.Embeddings;
using Lyntai.Embeddings.Model2Vec;
using Lyntai.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Embeddings;

/// <summary>The in-process, server-free embedder.
///
/// <para>Every test here builds its OWN model — a real safetensors file and a real WordPiece vocabulary,
/// written to a temp directory — so the suite needs no download and no server. That is also what makes the
/// arithmetic checkable: with a table whose rows are known, a mean-pooled vector has one right answer.</para>
/// </summary>
public class Model2VecProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lyntai-static-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a temp dir is not worth failing a run */ }
        GC.SuppressFinalize(this);
    }

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

        using (var file = File.Create(Path.Combine(_dir, "model.safetensors")))
        {
            file.Write(BitConverter.GetBytes((long)header.Length));
            file.Write(header);
            file.Write(payload);
        }

        File.WriteAllLines(Path.Combine(_dir, "vocab.txt"), vocabulary);
        if (normalize is { } n)
            File.WriteAllText(Path.Combine(_dir, "config.json"),
                JsonSerializer.Serialize(new Dictionary<string, object> { ["normalize"] = n }));
        return _dir;
    }

    /// <summary>A BERT vocabulary needs its special tokens present, and WordPiece needs [UNK].</summary>
    private static List<string> Vocabulary(params string[] words) =>
        ["[PAD]", "[UNK]", "[CLS]", "[SEP]", "[MASK]", .. words];

    [Fact]
    public void Declares_EMBED_and_nothing_else_so_a_router_never_sends_it_a_chat()
    {
        // An embedder is a provider like any other now (D128) — what makes it an embedder is the DECLARATION,
        // not a separate interface. Asserting the absences is the half that matters: it is what stops the
        // router dispatching a completion here and getting the default Unsupported back.
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha")));

        Assert.Equal([ProviderKinds.Text], embedder.Capabilities.Accepts);
        Assert.Equal([ProviderKinds.Vector], embedder.Capabilities.Produces);
        Assert.Equal([ProviderOperation.Complete], embedder.Capabilities.Operations);
        Assert.Equal("static", embedder.Id);
        Assert.True(embedder.IsAvailable);
    }

    [Fact]
    public void AddModel2Vec_registers_it_as_a_PROVIDER_as_well_as_the_embedder_slot()
    {
        // Both halves are load-bearing: the slot keeps the one-embedder deployment working untouched, and
        // the provider collection is what lets a second embedder be registered and told apart by id.
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddModel2VecProvider(WriteModel(Vocabulary("alpha"))));
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IEmbedder>());
        var asProvider = Assert.Single(
            provider.GetServices<IModelProvider>().Where(p => p.Id == "static"));
        Assert.Contains(ProviderKinds.Vector, asProvider.Capabilities.Produces);
    }

    [Fact]
    public void Reads_the_table_and_reports_the_models_own_width()
    {
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta"), dimensions: 7));

        Assert.Equal(7, embedder.Dimensions);
    }

    [Fact]
    public async Task Mean_pools_the_rows_its_tokens_select()
    {
        // Rows are all-i, so "alpha beta" (ids 5 and 6) means exactly (5 + 6) / 2 = 5.5 in every dimension.
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));

        var vector = (await embedder.EmbedAsync(["alpha beta"]))[0];

        Assert.All(vector, v => Assert.Equal(5.5f, v, 3));
    }

    [Fact]
    public async Task Pools_CONTENT_tokens_only_no_CLS_or_SEP_which_would_move_every_vector()
    {
        // PINNED because it is invisible if it changes. A model2vec table is built against the ids its own
        // pruned vocabulary produces; bracketing the input with [CLS]/[SEP] would fold two more rows into
        // every mean and shift each vector by an amount no test asserting "a vector came back" could see.
        // "alpha" alone must therefore be row 5 and nothing else.
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));

        var vector = (await embedder.EmbedAsync(["alpha"]))[0];

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
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha")));

        var vectors = await embedder.EmbedAsync(["", "   "]);

        Assert.All(vectors, v => Assert.All(v, component => Assert.Equal(0f, component)));
    }

    [Fact]
    public void A_MISSING_file_names_the_one_that_is_missing_rather_than_null_referencing()
    {
        // A partial download is the common failure and its unguarded symptom is a null reference far away.
        WriteModel(Vocabulary("alpha"));
        File.Delete(Path.Combine(_dir, "vocab.txt"));

        var error = Assert.Throws<FileNotFoundException>(() => Model2VecProvider.FromDirectory(_dir));
        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_safetensors_FAILS_rather_than_producing_plausible_vectors()
    {
        // The dangerous direction: a silently wrong table embeds fine and costs retrieval quality nobody
        // can trace back to it.
        WriteModel(Vocabulary("alpha"));
        File.WriteAllText(Path.Combine(_dir, "model.safetensors"), "this is not a tensor file at all");

        Assert.Throws<InvalidDataException>(() => Model2VecProvider.FromDirectory(_dir));
    }

    [Fact]
    public async Task Has_NO_context_limit_which_is_the_one_thing_this_class_has_over_a_small_transformer()
    {
        // Every sub-100 MB transformer embedder rejects an input past 512 tokens. A lookup table has no
        // positional embeddings, so a long document is just more rows to average.
        var embedder = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));

        var vector = (await embedder.EmbedAsync([string.Join(" ", Enumerable.Repeat("alpha beta", 4000))]))[0];

        Assert.Contains(vector, v => v != 0f);
    }
}

/// <summary>The static embedder against a REAL downloaded model, which the synthetic fixture cannot
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

        var embedder = Model2VecProvider.FromDirectory(Directory!);
        var vectors = await embedder.EmbedAsync([
            "the weather forecast for tomorrow",
            "a stock market share price quote",
            "tomorrow's weather forecast",
        ]);

        Assert.True(embedder.Dimensions > 0);

        // The table's own vocabulary is PRUNED, so a wrong row mapping would still produce finite vectors.
        // Ordering a related pair above an unrelated one is the cheapest check that cannot pass on one.
        var related = Cosine(vectors[0], vectors[2]);
        var unrelated = Cosine(vectors[0], vectors[1]);
        Assert.True(related > unrelated,
            $"related {related:F4} should outrank unrelated {unrelated:F4} — a wrong row mapping looks like this");
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
