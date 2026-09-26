using System.Text.Json;

namespace Lyntai.Text;

/// <summary>Where a model's special tokens go around one text or a pair, and which segment each part is in —
/// the post-processor a <c>tokenizer.json</c> declares. XLM-R's is <c>&lt;s&gt; A &lt;/s&gt;</c> and
/// <c>&lt;s&gt; A &lt;/s&gt;&lt;/s&gt; B &lt;/s&gt;</c>, every segment 0; BERT's puts <c>B [SEP]</c> in segment 1.</summary>
internal sealed class SequenceTemplate
{
    private readonly Part[] _single;
    private readonly Part[] _pair;

    private SequenceTemplate(Part[] single, Part[] pair)
    {
        _single = single;
        _pair = pair;
        SingleOverhead = single.Sum(p => p.Specials.Length);
        PairOverhead = pair.Sum(p => p.Specials.Length);
    }

    /// <summary>Special tokens around one text.</summary>
    public int SingleOverhead { get; }

    /// <summary>Special tokens around a pair.</summary>
    public int PairOverhead { get; }

    /// <summary>No post-processor: the text alone, and a pair's second side in segment 1, as the reference
    /// encodes a second sequence when nothing says otherwise.</summary>
    public static SequenceTemplate None { get; } =
        new([Part.Sequence('A', 0)], [Part.Sequence('A', 0), Part.Sequence('B', 1)]);

    /// <summary><c>cls A sep</c> and <c>cls A sep sep B sep</c>, every segment 0.</summary>
    public static SequenceTemplate Roberta(int cls, int sep) => new(
        [Part.Special(cls, 0), Part.Sequence('A', 0), Part.Special(sep, 0)],
        [Part.Special(cls, 0), Part.Sequence('A', 0), Part.Special(sep, 0),
         Part.Special(sep, 0), Part.Sequence('B', 0), Part.Special(sep, 0)]);

    /// <summary><c>cls A sep</c> and <c>cls A sep B sep</c>, with <c>B sep</c> in segment 1.</summary>
    public static SequenceTemplate Bert(int cls, int sep) => new(
        [Part.Special(cls, 0), Part.Sequence('A', 0), Part.Special(sep, 0)],
        [Part.Special(cls, 0), Part.Sequence('A', 0), Part.Special(sep, 0), Part.Sequence('B', 1), Part.Special(sep, 1)]);

    /// <summary>A <c>TemplateProcessing</c> node: its <c>single</c> and <c>pair</c> item lists, each special
    /// resolved through <c>special_tokens</c> to the ids it expands to.</summary>
    /// <exception cref="InvalidDataException">An item names a special the node does not define, or a sequence
    /// other than A (and B, in a pair).</exception>
    public static SequenceTemplate FromTemplateProcessing(JsonElement node)
    {
        var specials = node.GetProperty("special_tokens");
        return new(Parts(node.GetProperty("single"), specials, "A"), Parts(node.GetProperty("pair"), specials, "AB"));
    }

    public TokenEncoding Frame(IReadOnlyList<int> content) => Build(_single, content, []);

    public TokenEncoding Frame(IReadOnlyList<int> first, IReadOnlyList<int> second) => Build(_pair, first, second);

    private static TokenEncoding Build(Part[] parts, IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        var ids = new List<int>(a.Count + b.Count + 4);
        var types = new List<int>(ids.Capacity);
        foreach (var part in parts)
        {
            IReadOnlyList<int> values = part.Name switch { 'A' => a, 'B' => b, _ => part.Specials };
            ids.AddRange(values);
            types.AddRange(Enumerable.Repeat(part.TypeId, values.Count));
        }

        var mask = new int[ids.Count];
        Array.Fill(mask, 1);
        return new TokenEncoding([.. ids], mask, [.. types]);
    }

    private static Part[] Parts(JsonElement items, JsonElement specials, string sequences)
    {
        var parts = new List<Part>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("SpecialToken", out var special))
            {
                var name = special.GetProperty("id").GetString()!;
                if (!specials.TryGetProperty(name, out var entry))
                    throw new InvalidDataException($"tokenizer.json's template names a special token '{name}' it does not define.");
                parts.Add(new Part([.. entry.GetProperty("ids").EnumerateArray().Select(id => id.GetInt32())], '\0',
                    special.GetProperty("type_id").GetInt32()));
                continue;
            }

            var sequence = item.GetProperty("Sequence");
            var id = sequence.GetProperty("id").GetString();
            if (id is not { Length: 1 } || !sequences.Contains(id[0], StringComparison.Ordinal))
                throw new InvalidDataException($"tokenizer.json's template places a sequence '{id}' where only {sequences} may go.");
            parts.Add(Part.Sequence(id[0], sequence.GetProperty("type_id").GetInt32()));
        }
        return [.. parts];
    }

    /// <summary>One slot: special ids (<see cref="Name"/> <c>'\0'</c>), or sequence A or B.</summary>
    private readonly record struct Part(int[] Specials, char Name, int TypeId)
    {
        public static Part Special(int id, int typeId) => new([id], '\0', typeId);

        public static Part Sequence(char name, int typeId) => new([], name, typeId);
    }
}
