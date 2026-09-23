using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UseFileSystemStorage` shows up right on the builder.
namespace Lyntai;

/// <summary>
/// Storage as FILES — one record per file under a root you choose, each a small Markdown document whose
/// header holds the record's fields and whose body is its text, so the data reads without a client.
///
/// <para><b>What it serves — the domains a person reads:</b> <see cref="IKeyValueStore"/> (and with it live
/// model routing, which reads keys), <see cref="IPromptVersionStore"/>, <see cref="IConversationStore"/>,
/// <see cref="IMemoryStore"/> and <see cref="ICuratedMemoryStore"/>. Each passes the same cross-backend
/// contract as SQLite: a query finds the same entries here as there.</para>
///
/// <para><b>What it does not, and why — so compose another backend for them.</b> Jobs, usage counters, the
/// response cache and vectors are machine state nobody reads, which SQLite holds better; scores and traces are
/// evaluation output SQLite already exports. The memory ENGINE's graph store is the one gap that is not a
/// choice: it is the largest contract in the library, and not yet built here. A domain this registers
/// nothing for stays unresolvable — the same startup signal a disabled SQLite feature gives.</para>
///
/// <para><b>Registration uses <c>TryAdd</c>, like every storage backend here, so the FIRST registration of a
/// domain wins</b>: call this before <c>UseSqliteStorage</c> to take the domains it serves and let SQLite hold
/// the rest.</para>
/// </summary>
public static class FileSystemStorageBuilderExtensions
{
    /// <summary>Serve the domains above from files under <see cref="FileSystemStorageOptions.Root"/>.</summary>
    /// <exception cref="ArgumentException">No root was set.</exception>
    public static LyntaiBuilder UseFileSystemStorage(this LyntaiBuilder builder,
        Action<FileSystemStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new FileSystemStorageOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.Root))
            throw new ArgumentException(
                "FileSystemStorageOptions.Root is required — where your data lives is yours to choose.", nameof(configure));

        // Keyed by the full path, so each root is owned once per container and released when it is disposed.
        var path = Path.GetFullPath(options.Root);
        builder.Services.TryAddKeyedSingleton(path, (sp, _) => new FileSystemRoot(path,
            sp.GetService<ILoggerFactory>()?.CreateLogger("Lyntai.Storage.FileSystem")));
        FileSystemRoot Root(IServiceProvider sp) => sp.GetRequiredKeyedService<FileSystemRoot>(path);

        builder.Services.TryAddSingleton<IKeyValueStore>(sp => new FileSystemKeyValueStore(Root(sp)));
        builder.Services.TryAddSingleton<IPromptVersionStore>(sp => new FileSystemPromptVersionStore(Root(sp)));
        builder.Services.TryAddSingleton<IConversationStore>(sp => new FileSystemConversationStore(Root(sp)));
        builder.Services.TryAddSingleton<IMemoryStore>(sp =>
            new FileSystemMemoryStore(Root(sp), sp.GetRequiredService<LyntaiOptions>()));
        builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new FileSystemCuratedMemoryStore(Root(sp)));
        return builder;
    }
}
