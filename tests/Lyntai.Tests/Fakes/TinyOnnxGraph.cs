using System.Text;

namespace Lyntai.Tests.Fakes;

/// <summary>A one-node ONNX graph written byte by byte, so a test can hand the provider a real
/// <c>model.onnx</c> without a model on disk: <c>input_ids</c> (int64, <c>[batch, seq]</c>) cast to float and
/// returned as <see cref="Build"/>'s named output — a rank-2 output, which no per-token head can read.</summary>
public static class TinyOnnxGraph
{
    public static byte[] Build(string outputName)
    {
        var cast = Concat(
            Str(1, "input_ids"), Str(2, outputName), Str(4, "Cast"),
            Message(5, Concat(Str(1, "to"), Varint(3, 1), Varint(20, 2))));
        var graph = Concat(
            Message(1, cast), Str(2, "g"),
            Message(11, ValueInfo("input_ids", 7)),
            Message(12, ValueInfo(outputName, 1)));
        return Concat(Varint(1, 8), Message(7, graph), Message(8, Varint(2, 13)));
    }

    private static byte[] ValueInfo(string name, int elementType)
    {
        var shape = Concat(Message(1, Str(2, "batch")), Message(1, Str(2, "seq")));
        var tensor = Concat(Varint(1, elementType), Message(2, shape));
        return Concat(Str(1, name), Message(2, Message(1, tensor)));
    }

    private static byte[] Str(int field, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Concat(Tag(field, 2), VarintBytes(bytes.Length), bytes);
    }

    private static byte[] Message(int field, byte[] body) => Concat(Tag(field, 2), VarintBytes(body.Length), body);

    private static byte[] Varint(int field, long value) => Concat(Tag(field, 0), VarintBytes(value));

    private static byte[] Tag(int field, int wire) => VarintBytes((field << 3) | wire);

    private static byte[] VarintBytes(long value)
    {
        var bytes = new List<byte>();
        var v = (ulong)value;
        while (v >= 0x80) { bytes.Add((byte)(v | 0x80)); v >>= 7; }
        bytes.Add((byte)v);
        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
