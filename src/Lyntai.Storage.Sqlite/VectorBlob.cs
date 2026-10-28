using System.Buffers.Binary;
using System.Globalization;

namespace Lyntai.Storage.Sqlite;

/// <summary>The binary form of a stored vector: its components as little-endian IEEE-754 single-precision floats,
/// four bytes each, no header — so the dimension is the byte length over four. The bits are the float's own, which
/// is why a score read from a blob equals the one read from the JSON text it replaces.</summary>
internal static class VectorBlob
{
    public const int Width = sizeof(float);

    public static byte[] Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * Width];
        for (var i = 0; i < vector.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * Width), vector[i]);
        return bytes;
    }

    public static float[] Decode(ReadOnlySpan<byte> blob)
    {
        var vector = new float[blob.Length / Width];
        for (var i = 0; i < vector.Length; i++) vector[i] = Component(blob, i);
        return vector;
    }

    public static float Component(ReadOnlySpan<byte> blob, int index) =>
        BinaryPrimitives.ReadSingleLittleEndian(blob[(index * Width)..]);

    /// <summary>The components of a JSON array of numbers, read exactly as the scan always read them; false for
    /// text that is anything else.</summary>
    public static bool TryFromJson(string json, out float[] vector)
    {
        vector = [];
        var text = json.AsSpan();
        var open = text.IndexOf('[');
        var close = text.LastIndexOf(']');
        if (open < 0 || close < open) return false;
        text = text[(open + 1)..close];

        var values = new List<float>();
        while (text.Length > 0)
        {
            var comma = text.IndexOf(',');
            var token = comma < 0 ? text : text[..comma];
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
            values.Add(value);
            if (comma < 0) break;
            text = text[(comma + 1)..];
        }

        vector = [.. values];
        return true;
    }
}
