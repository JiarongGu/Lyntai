namespace Lyntai.Memory;

/// <summary>The exceptions an engine throws on purpose when the CALLER is wrong — an unknown ranking name, an
/// incoherent validity — which a fail-open path above it must let through, or the mistake reads as an empty recall.</summary>
internal static class MemoryCallerMistake
{
    /// <summary>True for a <see cref="KeyNotFoundException"/> or an <see cref="ArgumentException"/>.</summary>
    internal static bool Is(Exception ex) => ex is KeyNotFoundException or ArgumentException;
}
