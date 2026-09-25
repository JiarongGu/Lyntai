using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Memory.Verification;

/// <summary>Records which shipped registration filled the SINGULAR <see cref="IMemoryVerificationPolicy"/>
/// seam, so a second one fails at the line that made it instead of being dropped by <c>TryAdd</c> — which
/// left whichever came first verifying memory with nothing reporting the choice. A consumer's OWN policy is
/// not claimed, so one registered first still wins silently, as every seam here documents.</summary>
/// <param name="By">The registration that claimed the seam.</param>
internal sealed record MemoryVerificationClaim(string By)
{
    /// <summary>Claim the seam for <paramref name="by"/>, or throw naming the registration that already did.</summary>
    /// <exception cref="InvalidOperationException">A shipped verifier is already registered.</exception>
    internal static void Take(IServiceCollection services, string by)
    {
        var existing = services
            .Select(d => d.ServiceType == typeof(MemoryVerificationClaim) ? d.ImplementationInstance : null)
            .OfType<MemoryVerificationClaim>()
            .FirstOrDefault();
        if (existing is not null)
            throw new InvalidOperationException(
                $"{by} cannot register a memory verifier: {existing.By} already did. The seam is singular — " +
                "one judge per container — so register one of them, once.");
        services.AddSingleton(new MemoryVerificationClaim(by));
    }
}
