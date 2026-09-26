using System.Diagnostics;
using Lyntai.Text;

namespace Lyntai.Tests.Text;

/// <summary>SentencePiece's Unigram segmentation of one word, rule by rule. The golden fixture pins the whole
/// pipeline against the reference; these pin the rules a small vocabulary can isolate.</summary>
public class UnigramModelTests
{
    private const string Meta = "▁";

    /// <summary>Id 0 is the unknown piece, unmatchable, as in every real model; the given pieces follow from 1.</summary>
    private static UnigramModel Model(params (string Piece, double Score)[] pieces) =>
        new([("<unk>", 0.0), .. pieces], unknownId: 0, unmatchable: new HashSet<int> { 0 });

    private static int[] Segment(UnigramModel model, string word)
    {
        var ids = new List<int>();
        model.Segment(word, ids);
        return [.. ids];
    }

    [Fact]
    public void Takes_the_best_scoring_path_not_the_longest_first_match()
    {
        // [▁a, b] scores -2; [▁ab] scores -5
        var model = Model((Meta + "a", -1), ("b", -1), (Meta + "ab", -5), (Meta, -3), ("a", -3));

        Assert.Equal([1, 2], Segment(model, Meta + "ab"));
    }

    [Fact]
    public void A_tie_keeps_the_path_that_reached_the_end_FIRST_as_the_reference_does()
    {
        // [▁a, b] and [▁ab] both score -2; "▁ab" reaches the end while position 0 is scanned, and a later
        // equal score does not replace it
        var model = Model((Meta + "a", -1), ("b", -1), (Meta + "ab", -2), (Meta, -5), ("a", -5));

        Assert.Equal([3], Segment(model, Meta + "ab"));
    }

    [Fact]
    public void Consecutive_unknown_characters_FUSE_into_one_unknown_id()
    {
        var model = Model((Meta, -1), ("q", -2));

        Assert.Equal([1, 0, 2, 0], Segment(model, Meta + "xyzqxy"));
    }

    [Fact]
    public void A_surrogate_pair_is_ONE_code_point_so_one_unknown_not_two()
    {
        var model = Model((Meta, -1), ("z", -1));

        Assert.Equal([1, 0, 2], Segment(model, Meta + "\U0001F600z"));
    }

    [Fact]
    public void A_piece_never_ends_inside_a_surrogate_pair()
    {
        // a vocabulary row holding a lone high surrogate must not match half of a pair
        var model = Model((Meta, -1), ("\ud83d", -0.1));

        Assert.Equal([1, 0], Segment(model, Meta + "\U0001F600"));
    }

    [Fact]
    public void An_unmatchable_piece_is_never_chosen_from_text()
    {
        // "<s>" is a special token: excluded from matching, so the text "<s>" is its three characters
        var model = new UnigramModel(
            [("<unk>", 0.0), ("<s>", 0.0), ("<", -2), ("s", -2), (">", -2)],
            unknownId: 0, unmatchable: new HashSet<int> { 0, 1 });

        Assert.Equal([2, 3, 4], Segment(model, "<s>"));
    }

    [Fact]
    public void A_repeated_piece_resolves_to_its_LAST_id_as_the_reference_map_does()
    {
        var model = Model(("x", -1), ("x", -1));

        Assert.Equal([2], Segment(model, "x"));
    }

    [Fact]
    public void An_empty_word_adds_nothing() => Assert.Empty(Segment(Model(("a", -1)), string.Empty));

    [Fact]
    public void Appends_to_what_the_list_already_holds()
    {
        var ids = new List<int> { 99 };

        Model(("a", -1)).Segment("aa", ids);

        Assert.Equal([99, 1, 1], ids);
    }

    [Fact]
    public void A_very_long_word_is_linear_in_its_length_not_quadratic()
    {
        var model = Model(("a", -1), ("aa", -1.5), ("aaaa", -2.5));
        var clock = Stopwatch.StartNew();

        var ids = Segment(model, new string('a', 20_000));

        Assert.Equal(Enumerable.Repeat(3, 5_000), ids);   // every "aaaa": -2.5 per 4 beats -3.0 or -4.0
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
    }
}
