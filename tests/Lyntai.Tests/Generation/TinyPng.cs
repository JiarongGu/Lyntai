using System.IO.Compression;
using System.Text;

namespace Lyntai.Tests.Generation;

/// <summary>A small valid PNG built by hand for the pipeline tests: 8-bit RGB, a gradient shifted by a seed, so two
/// seeds are two different pictures. <paramref name="filter"/> 0 stores the rows as they are, 1 writes them
/// Sub-filtered and 2 Up-filtered — the same pixels three ways — and <paramref name="textChunk"/> adds an ancillary
/// chunk, which changes the file's bytes and no pixel.</summary>
internal static class TinyPng
{
    public static byte[] Make(int width, int height, int seed, int filter = 0, string? textChunk = null)
    {
        var pixels = new byte[height, width * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width * 3; x++)
                pixels[y, x] = (byte)(x * 7 + y * 13 + seed * 29);

        var rows = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var at = y * (1 + width * 3);
            rows[at] = (byte)filter;
            for (var x = 0; x < width * 3; x++)
            {
                var left = x >= 3 ? pixels[y, x - 3] : 0;
                var up = y > 0 ? pixels[y - 1, x] : 0;
                rows[at + 1 + x] = (byte)(pixels[y, x] - filter switch { 1 => left, 2 => up, _ => 0 });
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) z.Write(rows);

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8; header[9] = 2;
        Chunk(png, "IHDR", header);
        if (textChunk is not null) Chunk(png, "tEXt", Encoding.ASCII.GetBytes("prompt\0" + textChunk));
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream to, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        var word = new byte[4];
        WriteBigEndian(word, 0, data.Length);
        to.Write(word);
        to.Write(body);
        WriteBigEndian(word, 0, (int)Crc32(body));
        to.Write(word);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1)));
        }
        return ~crc;
    }

    private static void WriteBigEndian(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)(value >> 24); bytes[at + 1] = (byte)(value >> 16);
        bytes[at + 2] = (byte)(value >> 8); bytes[at + 3] = (byte)value;
    }
}
