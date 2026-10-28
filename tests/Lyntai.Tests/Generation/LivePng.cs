using System.IO.Compression;

namespace Lyntai.Tests.Generation;

/// <summary>What the image live suites read back from a rendered PNG without an image library.</summary>
internal static class LivePng
{
    /// <summary>IHDR width/height — big-endian at offsets 16 and 20. Fails the test when the bytes are no PNG.</summary>
    public static (int Width, int Height) Dimensions(byte[] png)
    {
        Assert.True(png.Length > 24 && png[0] == 0x89 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "not a PNG");
        return (ReadBigEndian(png, 16), ReadBigEndian(png, 20));
    }

    /// <summary>How much fine structure a render holds: the mean absolute difference between neighbouring pixels'
    /// luma, 0 to 255. A soft smear scores a few units and a sharp picture several times that, which is the
    /// difference a LoRA at four steps makes — a number a test can compare where "recognisable" is a judgement.</summary>
    public static double Detail(byte[] png)
    {
        var (width, height, luma) = Luma(png);
        double sum = 0;
        long count = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (x + 1 < width) { sum += Math.Abs(luma[y * width + x] - luma[y * width + x + 1]); count++; }
                if (y + 1 < height) { sum += Math.Abs(luma[y * width + x] - luma[(y + 1) * width + x]); count++; }
            }
        return sum / count;
    }

    /// <summary>The mean absolute luma difference between two renders of one size, 0 to 255 — how far apart they are.</summary>
    public static double Distance(byte[] a, byte[] b)
    {
        var (wa, ha, la) = Luma(a);
        var (wb, hb, lb) = Luma(b);
        Assert.Equal((wa, ha), (wb, hb));
        return la.Zip(lb, (p, q) => Math.Abs(p - q)).Average();
    }

    /// <summary>Writes a render under <c>LYNTAI_LIVE_OUTPUT_DIR</c> when that is set, so a person can look at what a
    /// live run produced; nothing is written otherwise.</summary>
    public static void Keep(string name, byte[] png)
    {
        if (Environment.GetEnvironmentVariable("LYNTAI_LIVE_OUTPUT_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), png);
    }

    /// <summary>The luma plane of an 8-bit, non-interlaced RGB or RGBA PNG, un-filtered by hand.</summary>
    private static (int Width, int Height, double[] Luma) Luma(byte[] png)
    {
        var (width, height) = Dimensions(png);
        var colorType = png[25];
        Assert.True(png[24] == 8 && colorType is 2 or 6 && png[28] == 0,
            "the live suites read only 8-bit, non-interlaced RGB or RGBA");
        var bytesPerPixel = colorType == 2 ? 3 : 4;

        using var idat = new MemoryStream();
        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = ReadBigEndian(png, at);
            if (png[at + 4] == 'I' && png[at + 5] == 'D' && png[at + 6] == 'A' && png[at + 7] == 'T')
                idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);

        var stride = width * bytesPerPixel;
        var data = raw.ToArray();
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var filter = data[y * (stride + 1)];
            var line = data.AsSpan(y * (stride + 1) + 1, stride).ToArray();
            var above = y > 0 ? rows[y - 1] : new byte[stride];
            for (var i = 0; i < stride; i++)
            {
                int left = i >= bytesPerPixel ? line[i - bytesPerPixel] : 0;
                int up = above[i];
                int upLeft = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
                line[i] = (byte)(line[i] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"PNG filter {filter}"),
                });
            }
            rows[y] = line;
        }

        var luma = new double[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = x * bytesPerPixel;
                luma[y * width + x] = 0.299 * rows[y][p] + 0.587 * rows[y][p + 1] + 0.114 * rows[y][p + 2];
            }
        return (width, height, luma);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadBigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
