namespace Lyntai.ImagePipeline;

/// <summary>A pose reference the sample draws itself: an OpenPose body skeleton from fixed keypoints, so the pose step
/// needs no person detector and no sampler to make its reference. Keypoints are the 18 of OpenPose's body model in its own
/// order, in a unit square (x right, y down), and <see cref="Render"/> paints them the way the OpenPose ControlNet was
/// trained to read them: a coloured limb per bone, a coloured dot per joint, on black.</summary>
public static class PoseReference
{
    /// <summary>OpenPose's 18 body keypoints, in its order. "Right" is the figure's own right, so it lies on the image's left.</summary>
    public static readonly IReadOnlyList<string> Joints =
    [
        "nose", "neck", "right-shoulder", "right-elbow", "right-wrist", "left-shoulder", "left-elbow", "left-wrist",
        "right-hip", "right-knee", "right-ankle", "left-hip", "left-knee", "left-ankle",
        "right-eye", "left-eye", "right-ear", "left-ear",
    ];

    /// <summary>The bones as pairs of indices into <see cref="Joints"/>, in OpenPose's drawing order (which fixes each limb's colour).</summary>
    public static readonly IReadOnlyList<(int From, int To)> Bones =
    [
        (1, 2), (1, 5), (2, 3), (3, 4), (5, 6), (6, 7), (1, 8), (8, 9), (9, 10), (1, 11), (11, 12), (12, 13),
        (1, 0), (0, 14), (14, 16), (0, 15), (15, 17),
    ];

    private static readonly (byte R, byte G, byte B)[] Colours =
    [
        (255, 0, 0), (255, 85, 0), (255, 170, 0), (255, 255, 0), (170, 255, 0), (85, 255, 0), (0, 255, 0), (0, 255, 85),
        (0, 255, 170), (0, 255, 255), (0, 170, 255), (0, 85, 255), (0, 0, 255), (85, 0, 255), (170, 0, 255), (255, 0, 255),
        (255, 0, 170), (255, 0, 85),
    ];

    /// <summary>A person standing square to the viewer with both arms out level, wrist to wrist spanning three quarters of the
    /// frame: the pose the compose step is asked to follow, and one no four-legged animal falls into by accident.</summary>
    public static readonly IReadOnlyList<(double X, double Y)> ArmsOutWide =
    [
        (0.50, 0.20), (0.50, 0.30),
        (0.40, 0.31), (0.27, 0.31), (0.14, 0.31),
        (0.60, 0.31), (0.73, 0.31), (0.86, 0.31),
        (0.45, 0.56), (0.44, 0.74), (0.44, 0.91),
        (0.55, 0.56), (0.56, 0.74), (0.56, 0.91),
        (0.48, 0.18), (0.52, 0.18), (0.45, 0.19), (0.55, 0.19),
    ];

    /// <summary>The keypoints as a reading, so a picture's pose can be measured against the reference it was asked to follow.</summary>
    public static PoseReading Reading(IReadOnlyList<(double X, double Y)> keypoints) =>
        new(keypoints.Select(k => ((double, double)?)k).ToList(), 1);

    /// <summary>The skeleton as an RGB PNG of <paramref name="size"/> by <paramref name="size"/>.</summary>
    public static byte[] Render(IReadOnlyList<(double X, double Y)> keypoints, int size = 1024)
    {
        if (keypoints.Count != Joints.Count) throw new ArgumentException($"expected {Joints.Count} keypoints, got {keypoints.Count}");
        var pixels = new byte[size * size * 3];
        var half = size / 170.0;

        for (var i = 0; i < Bones.Count; i++)
        {
            var (a, b) = (keypoints[Bones[i].From], keypoints[Bones[i].To]);
            Paint(pixels, size, a.X * size, a.Y * size, b.X * size, b.Y * size, half, Colours[i], 0.6);
        }
        for (var i = 0; i < keypoints.Count; i++)
            Paint(pixels, size, keypoints[i].X * size, keypoints[i].Y * size, keypoints[i].X * size, keypoints[i].Y * size, half, Colours[i], 1);
        return Png.Encode(size, size, pixels);
    }

    /// <summary>Lays a segment of half-width <paramref name="half"/> into the canvas: adds the colour scaled by
    /// <paramref name="weight"/> (OpenPose's own limb blend), capped at 255.</summary>
    private static void Paint(byte[] pixels, int size, double x0, double y0, double x1, double y1, double half, (byte R, byte G, byte B) colour, double weight)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - half)), maxX = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(x0, x1) + half));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - half)), maxY = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(y0, y1) + half));
        double dx = x1 - x0, dy = y1 - y0, length2 = dx * dx + dy * dy;
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var t = length2 == 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / length2, 0, 1);
                double px = x0 + t * dx - x, py = y0 + t * dy - y;
                if (px * px + py * py > half * half) continue;
                var at = (y * size + x) * 3;
                if (weight < 1)
                {
                    pixels[at] = (byte)Math.Min(255, pixels[at] + colour.R * weight);
                    pixels[at + 1] = (byte)Math.Min(255, pixels[at + 1] + colour.G * weight);
                    pixels[at + 2] = (byte)Math.Min(255, pixels[at + 2] + colour.B * weight);
                }
                else (pixels[at], pixels[at + 1], pixels[at + 2]) = colour;
            }
    }
}
