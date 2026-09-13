using System.Text.Json;

namespace Lyntai.Embeddings.Static;

/// <summary>The embedding matrix of a <c>model.safetensors</c> file, read once into managed memory.
///
/// <para><b>The whole format, because it is small enough to state:</b> eight little-endian bytes giving the
/// header length, that many bytes of JSON naming each tensor with its dtype, shape and byte range, then the
/// tensor data. A static model holds exactly one tensor — <c>potion-base-8M</c> is
/// <c>{"embeddings":{"dtype":"F32","shape":[29528,256],"data_offsets":[0,30236672]}}</c>.</para>
///
/// <para><b>Read eagerly rather than memory-mapped.</b> The table IS the model — every lookup touches it —
/// so a mapping buys nothing but a file handle held for the process's life, and the largest member of this
/// class is around 130 MB.</para></summary>
internal sealed class SafetensorsTable
{
    private readonly float[] _data;

    private SafetensorsTable(float[] data, int rows, int dimensions)
    {
        _data = data;
        Rows = rows;
        Dimensions = dimensions;
    }

    internal int Rows { get; }

    internal int Dimensions { get; }

    /// <summary>Add row <paramref name="row"/> into <paramref name="into"/>. Out-of-range rows are IGNORED
    /// rather than throwing: a tokenizer and a table can legitimately disagree at the edges (an added
    /// special token), and losing one token is the right cost — refusing the whole embedding is not.</summary>
    internal void AccumulateInto(int row, Span<float> into)
    {
        if (row < 0 || row >= Rows) return;
        var start = row * Dimensions;
        for (var i = 0; i < Dimensions; i++) into[i] += _data[start + i];
    }

    /// <summary>Read the single embedding tensor from <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not safetensors, holds no usable tensor, or holds
    /// one this reader cannot decode. Thrown rather than guessed at: a silently wrong table produces
    /// plausible vectors and a retrieval quality loss nobody can trace.</exception>
    internal static SafetensorsTable Load(string path)
    {
        using var file = File.OpenRead(path);

        Span<byte> lengthBytes = stackalloc byte[8];
        if (file.Read(lengthBytes) != 8) throw new InvalidDataException($"'{path}' is too short to be safetensors.");
        var headerLength = BitConverter.ToInt64(lengthBytes);
        if (headerLength <= 0 || headerLength > 64 * 1024 * 1024)
            throw new InvalidDataException($"'{path}' declares an implausible header length of {headerLength}.");

        var headerBytes = new byte[headerLength];
        file.ReadExactly(headerBytes);

        using var header = JsonDocument.Parse(headerBytes);

        // The FIRST float32 2-D tensor, by name where the conventional one exists. A static model carries a
        // single tensor, so this is a search over one entry in practice; scanning rather than demanding the
        // name means a model exported under a different key still loads.
        foreach (var entry in header.RootElement.EnumerateObject())
        {
            if (entry.Name == "__metadata__") continue;
            if (!entry.Value.TryGetProperty("dtype", out var dtype) || dtype.GetString() != "F32") continue;
            if (!entry.Value.TryGetProperty("shape", out var shape) || shape.GetArrayLength() != 2) continue;
            if (!entry.Value.TryGetProperty("data_offsets", out var offsets) || offsets.GetArrayLength() != 2) continue;

            var rows = shape[0].GetInt32();
            var dimensions = shape[1].GetInt32();
            var begin = offsets[0].GetInt64();
            var end = offsets[1].GetInt64();
            var expected = (long)rows * dimensions * sizeof(float);
            if (rows <= 0 || dimensions <= 0 || end - begin != expected)
                throw new InvalidDataException(
                    $"'{path}' tensor '{entry.Name}' declares {rows}x{dimensions} but spans {end - begin} "
                    + $"bytes, not {expected}.");

            var raw = new byte[expected];
            file.Position = 8 + headerLength + begin;
            file.ReadExactly(raw);

            // Safetensors is little-endian by definition; on a big-endian host the reinterpret below would
            // be silently wrong, which is the one failure a wrong vector cannot be told from.
            if (!BitConverter.IsLittleEndian)
                throw new InvalidDataException("safetensors is little-endian and this host is not.");

            var data = new float[rows * dimensions];
            Buffer.BlockCopy(raw, 0, data, 0, raw.Length);
            return new SafetensorsTable(data, rows, dimensions);
        }

        throw new InvalidDataException($"'{path}' holds no float32 2-D tensor to use as an embedding table.");
    }
}
