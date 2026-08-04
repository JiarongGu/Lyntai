using System.Diagnostics.CodeAnalysis;
using Lyntai.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Use*/Configure* methods appear on the builder alongside the others.
namespace Lyntai;

/// <summary>Wiring for provider LIFETIME. Relevant only when backend configuration is owned OUTSIDE the
/// deployment — by an end user, or a store the process polls. A deployment-configured app registers its
/// backends with <c>Add*</c> and never touches any of this; the defaults are already registered for it.
///
/// <para>Everything here registers a STRATEGY, never a call. That is the whole point of the seam: which
/// strategy is registered is the only thing that decides whether a backend is reused or rebuilt, so
/// switching costs one line at startup and no edit at any call site — including inside
/// <see cref="Lyntai.Generation.Routing.IGenerationRouterFactory"/> and
/// <see cref="Lyntai.Llm.Routing.ILlmRouterFactory"/>, which simply keep calling
/// <see cref="IProviderPool{TProvider}.GetOrAdd"/>.</para>
///
/// <para>The pool is registered as an OPEN generic, so one registration serves every provider seam —
/// <see cref="IProviderPool{TProvider}"/> of <see cref="Lyntai.Llm.ILlmProvider"/> and of
/// <see cref="Lyntai.Generation.IGenerationProvider"/> both resolve from it. Register a CLOSED
/// <c>IProviderPool&lt;T&gt;</c> before <c>AddLyntai</c> to override just one seam; the container prefers
/// the closed registration.</para></summary>
public static class ProviderPoolBuilderExtensions
{
    /// <summary>Reuse a backend instance while its configuration is unchanged, within the given bounds.
    /// This is already the default — call it to state the <see cref="ProviderPoolOptions"/> explicitly.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Bounds. Null keeps whatever bounds are already registered, or the
    /// <see cref="ProviderPoolOptions"/> defaults (64 entries, a 10-minute idle timeout). A non-null value
    /// REPLACES them, because passing bounds explicitly is a decision that has to win.</param>
    public static LyntaiBuilder UseProviderPool(this LyntaiBuilder builder, ProviderPoolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (options is not null)
        {
            builder.Services.RemoveAll<ProviderPoolOptions>();
            builder.Services.AddSingleton(options);
        }
        else
        {
            builder.Services.TryAddSingleton<ProviderPoolOptions>();
        }

        return UseStrategy(builder, typeof(BoundedProviderPool<>));
    }

    /// <summary>Build a FRESH backend instance on every call — for a host that wants its configuration
    /// re-read and applied each time.
    ///
    /// <para>Call sites do not change: the same <c>GetOrAdd</c>, and the same router factory above it, simply
    /// stops reusing. Dead-host cooldown and admission keep working, because both are keyed on the
    /// CONFIGURATION rather than on the instance — which is exactly what rebuilding a provider by hand gets
    /// wrong.</para></summary>
    /// <param name="builder">The builder.</param>
    public static LyntaiBuilder UseTransientProviders(this LyntaiBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return UseStrategy(builder, typeof(TransientProviderPool<>));
    }

    /// <summary>Bound how many calls may run against ONE configuration at a time — for a locally-run engine
    /// where simultaneous renders contend for a CPU or GPU. Unlimited by default.
    ///
    /// <para>Limits are declared per SLOT and enforced per KEY (see <see cref="ProviderAdmission"/>), and are
    /// applied by the router factories' POOLED overloads, which are the only ones that know a caller's
    /// configuration. Calling this more than once composes onto one
    /// <see cref="ProviderAdmissionOptions"/>, so a later call adds to the earlier one instead of replacing
    /// it.</para></summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Sets <see cref="ProviderAdmissionOptions.Default"/> or a per-slot limit.</param>
    public static LyntaiBuilder ConfigureProviderAdmission(
        this LyntaiBuilder builder, Action<ProviderAdmissionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // The singleton INSTANCE (not a factory) is what lets configure-time mutation be visible to the
        // resolved ProviderAdmission — the same immediate-mutation model GenerationOptions uses.
        var options = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(ProviderAdmissionOptions) && !d.IsKeyedService)
            ?.ImplementationInstance as ProviderAdmissionOptions;

        if (options is null)
        {
            options = new ProviderAdmissionOptions();
            builder.Services.AddSingleton(options);
        }

        configure(options);
        return builder;
    }

    /// <summary>Register one pooling strategy as THE strategy: the previous open-generic registration is
    /// removed rather than tried-and-skipped, because a <c>Use*</c> call is an explicit choice that has to
    /// win over a default another <c>Add*</c> already seeded (a generation registration seeds one before
    /// this could run).
    ///
    /// <para>Only the OPEN generic is removed. A host that registered a closed
    /// <c>IProviderPool&lt;T&gt;</c> of its own keeps it — the container prefers a closed registration, and
    /// silently discarding one here would make a deliberate host override vanish.</para></summary>
    private static LyntaiBuilder UseStrategy(
        LyntaiBuilder builder,
        // the trimmer has to see that this type's constructors survive — DI activates it by reflection
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type strategy)
    {
        builder.Services.RemoveAll(typeof(IProviderPool<>));
        builder.Services.AddSingleton(typeof(IProviderPool<>), strategy);
        return builder;
    }
}
