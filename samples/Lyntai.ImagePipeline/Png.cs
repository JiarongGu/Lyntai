using System.IO.Compression;
using System.Security.Cryptography;

namespace Lyntai.ImagePipeline;

/// <summary>What the sample reads back from a PNG without an image library: its size, and a hash of its PIXELS —
/// the file's own bytes carry ComfyUI's embedded workflow text, whose uploaded file names differ run to run.</summary>
public static class Png
{
    public static (int Width, int Height) Dimensions(byte[] png)
    {
        if (png.Length < 33 || png[0] != 0x89 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
            throw new InvalidDataException("not a PNG");
        return (BigEndian(png, 16), BigEndian(png, 20));
    }

    /// <summary>The sha256 of the decoded pixel rows (see <see cref="Decode"/>).</summary>
    public static string PixelsSha256(byte[] png) => Convert.ToHexStringLower(SHA256.HashData(Decode(png).Pixels));

    /// <summary>The pixel rows of an 8-bit, non-interlaced RGB or RGBA PNG — as ComfyUI saves one — with the row filters undone.</summary>
    public static (int Width, int Height, int BytesPerPixel, byte[] Pixels) Decode(byte[] png)
    {
        var (width, height) = Dimensions(png);
        if (png[24] != 8 || png[25] is not (2 or 6) || png[28] != 0)
            throw new NotSupportedException("only 8-bit, non-interlaced RGB or RGBA PNGs are decoded");
        var bytesPerPixel = png[25] == 2 ? 3 : 4;

        using var idat = new MemoryStream();
        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = BigEndian(png, at);
            if (png[at + 4] == 'I' && png[at + 5] == 'D' && png[at + 6] == 'A' && png[at + 7] == 'T')
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);

        var stride = width * bytesPerPixel;
        var scan = raw.ToArray();
        var pixels = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var filter = scan[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                var left = x >= bytesPerPixel ? pixels[y * stride + x - bytesPerPixel] : 0;
                var up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
                var upLeft = y > 0 && x >= bytesPerPixel ? pixels[(y - 1) * stride + x - bytesPerPixel] : 0;
                var value = scan[y * (stride + 1) + 1 + x];
                pixels[y * stride + x] = (byte)(value + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"unknown PNG filter {filter}"),
                });
            }
        }
        return (width, height, bytesPerPixel, pixels);
    }

    /// <summary>An 8-bit RGB PNG from <paramref name="rgb"/> (three bytes per pixel, row by row), unfiltered and deflated.</summary>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        var stride = width * 3;
        var scan = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(rgb, y * stride, scan, y * (stride + 1) + 1, stride);

        using var deflated = new MemoryStream();
        using (var z = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true)) z.Write(scan);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", deflated.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(body));
        stream.Write(length);
        stream.Write(body);
        stream.Write(crc);
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
        bytes[at] = (byte)(value >> 24);
        bytes[at + 1] = (byte)(value >> 16);
        bytes[at + 2] = (byte)(value >> 8);
        bytes[at + 3] = (byte)value;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int BigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
