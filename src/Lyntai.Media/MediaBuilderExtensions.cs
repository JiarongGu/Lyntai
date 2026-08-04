using Lyntai.Media;
using Lyntai.Media.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Add*/Use* methods appear on the builder.
namespace Lyntai;

/// <summary>Platform configuration for the media domain.</summary>
public sealed class MediaOptions
{
    /// <summary>Candidate order used when a caller doesn't name one — the media counterpart of
    /// <c>LyntaiOptions.DefaultCandidates</c>, and mutable for the same reason: the builder sets it at
    /// configure time.</summary>
    public List<MediaCandidate> DefaultCandidates { get; } = [];
}

public static class MediaBuilderExtensions
{
    /// <summary>Register a media backend into the <see cref="IMediaProvider"/> collection. Adding a backend is
    /// one registration — never an edit to a branch inside an existing provider (which is exactly the shape a
    /// sibling app grew: one class with an <c>if (provider == "automatic1111")</c> inside).</summary>
    public static LyntaiBuilder AddMediaProvider(
        this LyntaiBuilder builder, Func<IServiceProvider, IMediaProvider> factory)
    {
        builder.Services.AddSingleton(factory);
        MediaOptionsFor(builder);   // ensure the options singleton exists even with no candidates configured
        builder.Services.TryAddSingleton<IMediaRouter>(sp => new MediaRouter(sp.GetServices<IMediaProvider>()));
        return builder;
    }

    /// <summary>Set the media candidate order used when a caller doesn't pass one. SETS (clears + replaces) —
    /// the last call wins, it does not append — matching <c>LyntaiBuilder.UseDefaultCandidates</c> exactly, so
    /// the two domains behave identically. Each entry is a provider id, optionally <c>"provider:model"</c>.</summary>
    public static LyntaiBuilder UseDefaultMediaCandidates(this LyntaiBuilder builder, params string[] candidates)
    {
        var options = MediaOptionsFor(builder);
        options.DefaultCandidates.Clear();
        options.DefaultCandidates.AddRange(candidates.Select(Parse));
        return builder;

        static MediaCandidate Parse(string spec)
        {
            var at = spec.IndexOf(':');
            return at < 0
                ? new MediaCandidate(spec.Trim())
                : new MediaCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
        }
    }

    /// <summary>The single <see cref="MediaOptions"/> instance for this builder, registered as a singleton
    /// INSTANCE. Registering the instance (not a factory) is what lets configure-time mutation be visible to
    /// the resolved service — the same immediate-mutation model the builder's own <c>Options</c> uses.</summary>
    private static MediaOptions MediaOptionsFor(LyntaiBuilder builder)
    {
        foreach (var descriptor in builder.Services)
            if (descriptor.ServiceType == typeof(MediaOptions) &&
                descriptor.ImplementationInstance is MediaOptions existing)
                return existing;

        var created = new MediaOptions();
        builder.Services.AddSingleton(created);
        return created;
    }
}
