using System.Globalization;

namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IPromptVersionStore"/> as <c>prompts/&lt;name&gt;/v0001.md</c>, one file per revision
/// with the template as its body, plus <c>active.md</c> naming the revision in use.
/// <para><b>The active revision is a POINTER file, not a flag on each revision</b>, so saving or rolling back
/// rewrites exactly one file — a flag would need two writes, and a crash between them leaves no revision
/// active or two.</para></summary>
internal sealed class FileSystemPromptVersionStore(FileSystemRoot root, Func<DateTimeOffset>? clock = null)
    : IPromptVersionStore
{
    private const string ActiveFile = "active.md";

    private sealed class Prompt(string directory)
    {
        public string Directory { get; } = directory;
        public List<PromptVersion> Versions { get; } = [];
        public int? Active { get; set; }
    }

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("prompts");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private Dictionary<string, Prompt>? _prompts;

    private Dictionary<string, Prompt> Prompts()
    {
        if (_prompts is not null) return _prompts;
        var prompts = new Dictionary<string, Prompt>(StringComparer.Ordinal);
        if (Directory.Exists(_directory))
            foreach (var dir in Directory.EnumerateDirectories(_directory).Order(StringComparer.Ordinal))
                foreach (var (name, version, active) in root.Load(dir, (_, h, body) => (
                             Name: h.RequiredString("name"),
                             Version: h.Long("version") is { } v
                                 ? new PromptVersion(h.RequiredString("name"), (int)v, body, h.String("author"),
                                     h.Time("created") ?? throw new FormatException("'created' is required"), false)
                                 : null,
                             Active: h.Long("active"))))
                {
                    if (!prompts.TryGetValue(name, out var prompt)) prompts[name] = prompt = new Prompt(dir);
                    if (version is not null) prompt.Versions.Add(version);
                    if (active is { } a) prompt.Active = (int)a;
                }
        return _prompts = prompts;
    }

    private static PromptVersion Mark(Prompt p, PromptVersion v) => v with { IsActive = v.Version == p.Active };

    public Task<PromptVersion?> GetActiveAsync(string name, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var hit = Prompts().TryGetValue(name, out var p) ? p.Versions.FirstOrDefault(v => v.Version == p.Active) : null;
            return Task.FromResult(hit is null ? null : Mark(p!, hit));
        }
    }

    public Task<PromptVersion> SaveAsync(string name, string template, string? author = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(template);
        lock (_lock)
        {
            var prompts = Prompts();
            if (!prompts.TryGetValue(name, out var p))
                p = new Prompt(Path.Combine(_directory, RecordName.For(name)));
            RefuseUnreadPointer(p);

            // past every revision FILE too — one that did not parse still holds its number
            var next = Math.Max(p.Versions.Select(v => v.Version).DefaultIfEmpty(0).Max(),
                (int)FileSystemRoot.MaxId(p.Directory, prefix: "v")) + 1;
            var version = new PromptVersion(name, next, template, author, _clock(), IsActive: true);
            root.Write(Path.Combine(p.Directory, $"v{version.Version.ToString("D4", CultureInfo.InvariantCulture)}.md"),
                RecordFile.Write(new RecordHeader().Add("name", name).Add("version", version.Version)
                    .Add("author", author).Add("created", version.CreatedAt), template));
            WriteActive(p, name, version.Version);

            p.Versions.Add(version with { IsActive = false });
            prompts[name] = p;
            return Task.FromResult(version);
        }
    }

    public Task<IReadOnlyList<PromptVersion>> HistoryAsync(string name, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<PromptVersion> history = Prompts().TryGetValue(name, out var p)
                ? [.. p.Versions.OrderByDescending(v => v.Version).Select(v => Mark(p, v))]
                : [];
            return Task.FromResult(history);
        }
    }

    public Task<PromptVersion?> RollbackAsync(string name, int version, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!Prompts().TryGetValue(name, out var p)) return Task.FromResult<PromptVersion?>(null);
            var hit = p.Versions.FirstOrDefault(v => v.Version == version);
            if (hit is null) return Task.FromResult<PromptVersion?>(null);
            RefuseUnreadPointer(p);
            WriteActive(p, name, version);
            return Task.FromResult<PromptVersion?>(Mark(p, hit));
        }
    }

    // An active.md that exists but did not load is a person's edit (D171): never written over.
    private static void RefuseUnreadPointer(Prompt p)
    {
        var pointer = Path.Combine(p.Directory, ActiveFile);
        if (p.Active is null && File.Exists(pointer))
            throw new InvalidOperationException(
                $"'{pointer}' exists but names no readable active revision — repair or remove it");
    }

    private void WriteActive(Prompt p, string name, int version)
    {
        root.Write(Path.Combine(p.Directory, ActiveFile),
            RecordFile.Write(new RecordHeader().Add("name", name).Add("active", version), ""));
        p.Active = version;
    }
}
