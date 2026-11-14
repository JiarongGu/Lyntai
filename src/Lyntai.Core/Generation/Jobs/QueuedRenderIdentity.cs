using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Generation.Jobs;

/// <summary>The one identity of a queued render, (backend id, operation id), as the job engine and
/// <c>generate_fetch</c> name it: its bill's key in an <see cref="Lyntai.Inference.Budgeting.IIdempotentUsageTracker"/>
/// and the job id a tool's delivery carries. Both callers name it alike, so a render fetched by a job and by the tool
/// is billed once.</summary>
internal static class QueuedRenderIdentity
{
    /// <summary>The bill's key: <c>generation:&lt;backend id&gt;:&lt;operation id&gt;</c>, with the backend id as the
    /// provider registered it.</summary>
    public static string SpendKey(string providerId, string operationId) => $"generation:{providerId}:{operationId}";

    /// <summary>A GUID that is a function of the render alone (RFC 9562's version 8, from SHA-256 of
    /// <see cref="SpendKey"/>), so every fetch of one operation delivers under one job id.</summary>
    public static Guid DeliveryJobId(string providerId, string operationId)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(SpendKey(providerId, operationId)), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);   // version 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);   // the RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
