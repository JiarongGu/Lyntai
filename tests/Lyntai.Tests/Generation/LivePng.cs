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

    private static int ReadBigEndian(byte[] bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
}
