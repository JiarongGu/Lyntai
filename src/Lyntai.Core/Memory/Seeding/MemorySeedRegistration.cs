using Lyntai.Memory.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai;

/// <summary>Registers the retrieval CHANNELS a graph recall gathers candidates from
/// (<see cref="IMemorySeedSource"/>). <c>AddMemoryEngine</c> already registers the lexical and subject
/// channels; these two calls add the vector one and re-configure either.</summary>
public static class MemorySeedRegistration
{
    /// <summary>
    /// Let a recall reach an entry through MEANING — the query is embedded and its nearest entries join the
    /// candidate set, carrying their position in cosine order as a rank of their own.
    ///
    /// <para><b>Opt-in, and that is a deliberate asymmetry with the subject channel.</b> An
    /// <see cref="Lyntai.Embeddings.IEmbedder"/> is registered for reasons of its own — novelty, similarity
    /// linking — so seeding recall from it would change engines that never asked. A subject, by contrast,
    /// exists only because an annotator was already paid for.</para>
    ///
    /// <para><b>Needs an <see cref="Lyntai.Embeddings.IEmbedder"/> and an
    /// <see cref="Lyntai.Memory.IVectorStore"/> in the container</b>; without them the container fails to
    /// build, naming the missing one, rather than registering a channel that could only ever return
    /// nothing.</para>
    ///
    /// <para><b>Calling it twice yields ONE channel.</b> Two sources sharing a
    /// <see cref="IMemorySeedSource.Name"/> would each contribute a rank-fusion term for the same evidence,
    /// so the engine refuses the pair; the last <paramref name="options"/> supplied wins.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="options">Knobs; null takes the defaults, or a <see cref="SemanticSeedOptions"/> already
    /// registered in the container.</param>
    public static LyntaiBuilder AddMemorySemanticSeeds(this LyntaiBuilder builder,
        SemanticSeedOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Only when the caller supplied one: overwriting a SemanticSeedOptions the consumer registered
        // themselves, from a call that named nothing, is the silent-override this subsystem refuses
        // everywhere else.
        if (options is not null) builder.Services.AddSingleton(options);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMemorySeedSource, SemanticSeedSource>());
        return builder;
    }

    /// <summary>
    /// Configure the SUBJECT channel — the entries recorded under whichever annotated handles the query
    /// names, the only channel that reaches an entry through what it is ABOUT rather than through its own
    /// text.
    ///
    /// <para><b>It configures rather than adds</b>, because <c>AddMemoryEngine</c> already registers this
    /// channel: a handle exists only where an annotator was registered and paid for, so reading one back is
    /// on by default. Two sources under one name would double-count in fusion, so this call converges on the
    /// one source whichever side of <c>AddMemoryEngine</c> it is made.</para>
    ///
    /// <para><b><see cref="SubjectSeedOptions.K"/> or <see cref="SubjectSeedOptions.Scan"/> at <c>0</c> is
    /// the supported way OFF</b>, and it is a design fact rather than an oversight — see that record's own
    /// remarks. Not registering the channel at all is the other way, and the two agree: both leave a recall
    /// unable to reach an entry through a handle. Only the second is reported as a wiring finding, because
    /// only it can happen without anyone choosing it.</para>
    /// </summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="options">Knobs; null takes the defaults, or a <see cref="SubjectSeedOptions"/> already
    /// registered in the container.</param>
    public static LyntaiBuilder AddMemorySubjectSeeds(this LyntaiBuilder builder,
        SubjectSeedOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (options is not null) builder.Services.AddSingleton(options);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IMemorySeedSource, SubjectSeedSource>());
        return builder;
    }
}
