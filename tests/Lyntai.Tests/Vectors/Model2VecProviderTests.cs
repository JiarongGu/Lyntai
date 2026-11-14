using System.Text;
using Lyntai.Providers.Model2Vec;
using Lyntai.Inference;
using Lyntai.Text;
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

    /// <summary>Writes a model whose row <c>i</c> is all-<c>i</c>, so a mean over known ids is exact. <paramref name="maxLength"/> is
    /// the JSON value <c>config.json</c> states for <c>max_length</c> (<c>"null"</c> included); null states none.</summary>
    private string WriteModel(IReadOnlyList<string> vocabulary, int dimensions = 4, bool? normalize = false,
        IReadOnlyList<string>? wordsPastTheTable = null, string? maxLength = null)
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

        File.WriteAllLines(Path.Combine(Dir, "vocab.txt"), [.. vocabulary, .. wordsPastTheTable ?? []]);
        if (normalize is { } n)
            File.WriteAllText(Path.Combine(Dir, "config.json"),
                "{\"normalize\":" + (n ? "true" : "false") + (maxLength is null ? "" : ",\"max_length\":" + maxLength) + "}");
        return Dir;
    }

    /// <summary><paramref name="count"/> copies of each word in turn, space-separated: "a a a b b b".</summary>
    private static string Runs(int count, params string[] words) =>
        string.Join(" ", words.SelectMany(w => Enumerable.Repeat(w, count)));

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

        Assert.Equal(vectorProvider.Dimensions, vector.Length);
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

        Assert.Equal(vectorProvider.Dimensions, vector.Length);
        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task A_token_past_the_table_adds_no_row_and_is_not_counted()
    {
        // vocab.txt is longer than the table: "gamma" is id 7 and the table holds rows 0..6. The mean is over
        // the rows READ — (5 + 6) / 2 — not over three tokens, which would dilute every vector by what the
        // table cannot supply. normalize:false, so the dilution shows in the magnitude.
        var vectorProvider = Model2VecProvider.FromDirectory(
            WriteModel(Vocabulary("alpha", "beta"), normalize: false, wordsPastTheTable: ["gamma"]));

        var vector = (await vectorProvider.EmbedAsync(["alpha beta gamma"]))[0];

        Assert.All(vector, v => Assert.Equal(5.5f, v, 3));
    }

    [Fact]
    public async Task A_text_of_only_tokens_past_the_table_is_the_zero_vector()
    {
        var vectorProvider = Model2VecProvider.FromDirectory(
            WriteModel(Vocabulary("alpha", "beta"), normalize: false, wordsPastTheTable: ["gamma"]));

        var vector = (await vectorProvider.EmbedAsync(["gamma"]))[0];

        Assert.All(vector, component => Assert.Equal(0f, component));
    }

    [Fact]
    public async Task An_unknown_word_does_not_move_the_vector()
    {
        // The reference model2vec drops the unknown id before pooling (StaticModel.tokenize). [UNK] is row 1 here,
        // so averaging it in would read (5 + 1) / 2 = 3 instead of alpha's own 5.
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta"), normalize: false));

        var vector = (await vectorProvider.EmbedAsync(["alpha zzzz"]))[0];

        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task An_all_unknown_text_is_the_zero_vector()
    {
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta"), normalize: true));

        var vector = (await vectorProvider.EmbedAsync(["zzzz qqqq"]))[0];

        Assert.All(vector, component => Assert.Equal(0f, component));
    }

    [Fact]
    public async Task IncludeUnknownToken_keeps_the_old_mean()
    {
        var vectorProvider = Model2VecProvider.FromDirectory(
            WriteModel(Vocabulary("alpha", "beta"), normalize: false),
            new Model2VecProviderOptions { IncludeUnknownToken = true });

        var mixed = (await vectorProvider.EmbedAsync(["alpha zzzz"]))[0];
        var unknown = (await vectorProvider.EmbedAsync(["zzzz"]))[0];

        Assert.All(mixed, v => Assert.Equal(3f, v, 3));
        Assert.All(unknown, v => Assert.Equal(1f, v, 3));
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

        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, v => Assert.Equal(vectorProvider.Dimensions, v.Length));
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

    [Fact] // a download cut short is the common partial-file case, and the documented failure is InvalidDataException
    public void A_truncated_safetensors_file_fails_as_InvalidDataException_not_EndOfStream()
    {
        WriteModel(Vocabulary("alpha", "beta"));
        var path = Path.Combine(Dir, "model.safetensors");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^8]);

        Assert.Throws<InvalidDataException>(() => Model2VecProvider.FromDirectory(Dir));
    }

    // The reference model2vec (StaticModel, v0.10.0) cuts a text before it pools: to its first max_length × median-token-length
    // characters, then to its first max_length tokens, and only then drops the unknown id. A run of a's then a run of b's tells a cut
    // from none: a's alone mean 5 and the whole text (5 + 6) / 2 = 5.5. Vocabulary("a", "b") has token lengths 5,5,5,5,6,1,1, so its
    // median is 5.

    [Fact]
    public async Task Cuts_a_text_at_512_tokens_when_the_config_names_no_max_length()
    {
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("a", "b")));

        // 1024 tokens in 2047 characters, inside the 512 × 5 the character cut keeps, so the token cut decides
        var vector = (await vectorProvider.EmbedAsync([Runs(512, "a", "b")]))[0];

        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task Cuts_at_the_configs_max_length_and_a_null_one_cuts_nothing()
    {
        var three = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("a", "b"), maxLength: "3"));
        Assert.All((await three.EmbedAsync(["a a a b b b"]))[0], v => Assert.Equal(5f, v, 3));

        var none = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("a", "b"), maxLength: "null"));
        Assert.All((await none.EmbedAsync([Runs(4000, "a", "b")]))[0], v => Assert.Equal(5.5f, v, 3));
    }

    [Fact]
    public async Task Drops_the_unknown_token_after_the_cut_as_the_reference_does()
    {
        // [UNK] a b, cut to two tokens, is [UNK] a, which pools as a alone; dropping [UNK] first would keep a b and read 5.5
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("a", "b"), maxLength: "2"));

        var vector = (await vectorProvider.EmbedAsync(["z a b"]))[0];

        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task Cuts_the_characters_first_at_max_length_times_the_vocabularys_median_token_length()
    {
        // token lengths 5,1,1,7,7,7: an even count, so the median is the mean of 5 and 7, truncated — 6, where the lower middle would
        // be 5 and the upper 7. With max_length 2 the character cut keeps 12: "z…z a" of 12 keeps its a (row 1), of 13 does not.
        var vectorProvider = Model2VecProvider.FromDirectory(
            WriteModel(["[UNK]", "a", "b", "ccccccc", "ddddddd", "eeeeeee"], maxLength: "2"));

        var vectors = await vectorProvider.EmbedAsync([new string('z', 10) + " a", new string('z', 11) + " a"]);

        Assert.All(vectors[0], v => Assert.Equal(1f, v, 3));
        Assert.All(vectors[1], v => Assert.Equal(0f, v));
    }

    [Fact]
    public async Task The_character_cut_counts_code_points_as_the_reference_does()
    {
        // eight parrots (one unknown word, 16 UTF-16 units), alpha and beta are three tokens, so max_length 3 cuts no token, and the
        // character cut keeps 15. Counted in code points it ends after "alpha " and pools alpha (5); uncut, beta stays in (5.5);
        // counted in units it would end inside the eighth parrot and keep nothing (0)
        var vectorProvider = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta"), maxLength: "3"));

        var vector = (await vectorProvider.EmbedAsync([string.Concat(Enumerable.Repeat("\U0001F99C", 8)) + " alpha beta"]))[0];

        Assert.All(vector, v => Assert.Equal(5f, v, 3));
    }

    [Fact]
    public async Task EmbedWholeText_pools_every_token_as_earlier_releases_did()
    {
        var whole = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")),
            new Model2VecProviderOptions { EmbedWholeText = true });
        var cut = Model2VecProvider.FromDirectory(WriteModel(Vocabulary("alpha", "beta")));
        var text = Runs(4000, "alpha", "beta");

        Assert.All((await whole.EmbedAsync([text]))[0], v => Assert.Equal(5.5f, v, 3));
        Assert.All((await cut.EmbedAsync([text]))[0], v => Assert.Equal(5f, v, 3));
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

    [SkippableFact]
    public async Task An_unknown_word_does_not_move_a_real_models_vector()
    {
        Skip.If(string.IsNullOrWhiteSpace(Directory), "set LYNTAI_STATIC_MODEL_DIR to a model2vec directory");

        // vacuous unless this vocabulary really pieces the word as unknown, so that is asserted first
        var tokenizer = WordPieceTokenizer.FromModelDirectory(Directory!);
        Assert.Equal([tokenizer.UnknownId], tokenizer.EncodeToIds("🦜"));

        var vectorProvider = Model2VecProvider.FromDirectory(Directory!);
        var vectors = await vectorProvider.EmbedAsync(["the weather forecast for tomorrow", "the weather forecast for tomorrow 🦜"]);

        Assert.Equal(vectors[0], vectors[1]);
    }

    [SkippableFact]
    public async Task A_text_past_512_tokens_embeds_as_its_first_512_do()
    {
        Skip.If(string.IsNullOrWhiteSpace(Directory), "set LYNTAI_STATIC_MODEL_DIR to a model2vec directory");

        // vacuous unless the prefix is exactly 512 tokens and the tail adds more, so both are asserted first
        string[] cycle = ["the", "cat", "sat", "on", "a", "mat"];
        var prefix = string.Join(" ", Enumerable.Range(0, 512).Select(i => cycle[i % cycle.Length]));
        var text = prefix + string.Concat(Enumerable.Repeat(" stock market share price quote", 50));
        var tokenizer = WordPieceTokenizer.FromModelDirectory(Directory!);
        Assert.Equal(512, tokenizer.EncodeToIds(prefix).Count);
        Assert.True(tokenizer.EncodeToIds(text).Count > 512);

        var cut = await Model2VecProvider.FromDirectory(Directory!).EmbedAsync([prefix, text]);
        var whole = await Model2VecProvider.FromDirectory(Directory!, new Model2VecProviderOptions { EmbedWholeText = true })
            .EmbedAsync([text]);

        Assert.Equal(cut[0], cut[1]);
        Assert.NotEqual(cut[1], whole[0]);
    }
}
