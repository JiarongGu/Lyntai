namespace Lyntai.Storage.FileSystem;

/// <summary><see cref="IConversationStore"/> as <c>conversations/&lt;thread&gt;/</c>: <c>thread.md</c>
/// holds the thread, and each event is <c>000001.md</c> by its sequence number with the payload as its body.
/// <para><b>Threads load on first use; a thread's events load the first time that thread is read</b>, because
/// nothing here reads across threads and a store may hold far more events than it will ever be asked
/// for.</para></summary>
internal sealed class FileSystemConversationStore(FileSystemRoot root, Func<DateTimeOffset>? clock = null)
    : IConversationStore
{
    private const string ThreadFile = "thread.md";

    private sealed class Thread(ChatThread value, string directory)
    {
        public ChatThread Value { get; set; } = value;
        public string Directory { get; } = directory;
        public List<ChatMessage>? Messages { get; set; }
    }

    private readonly Lock _lock = new();
    private readonly string _directory = root.Combine("conversations");
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private Dictionary<string, Thread>? _threads;

    private Dictionary<string, Thread> Threads()
    {
        if (_threads is not null) return _threads;
        var threads = new Dictionary<string, Thread>(StringComparer.Ordinal);
        if (Directory.Exists(_directory))
            foreach (var dir in Directory.EnumerateDirectories(_directory).Order(StringComparer.Ordinal))
                if (root.TryLoad(Path.Combine(dir, ThreadFile), (_, h, _) => new ChatThread(h.RequiredString("id"),
                        h.String("title"), h.Time("created") ?? throw new FormatException("'created' is required"),
                        h.String("metadata")), out var thread))
                    threads[thread.Id] = new Thread(thread, dir);
        return _threads = threads;
    }

    private List<ChatMessage> Messages(Thread t) => t.Messages ??= [.. root.Load(t.Directory, (file, h, body) =>
            Path.GetFileName(file) == ThreadFile
                ? null
                : new ChatMessage(h.RequiredString("id"), t.Value.Id, h.Long("seq") ?? throw new FormatException("'seq' is required"),
                    h.RequiredString("kind"), body, h.String("metadata"),
                    h.Time("created") ?? throw new FormatException("'created' is required")))
        .OfType<ChatMessage>()
        .OrderBy(m => m.Seq)];

    private void WriteThread(Thread t) =>
        root.Write(Path.Combine(t.Directory, ThreadFile), RecordFile.Write(new RecordHeader()
            .Add("id", t.Value.Id).Add("title", t.Value.Title).Add("created", t.Value.CreatedAt)
            .Add("metadata", t.Value.Metadata), ""));

    public Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_lock)
        {
            var threads = Threads();
            // THROW like the SQL backends' primary-key violation — silently replacing a thread would keep the
            // old one's events under a new header.
            if (threads.ContainsKey(id)) throw new InvalidOperationException($"a thread '{id}' already exists");
            // A directory on disk that did not load is this id's thread with a thread.md that failed to parse:
            // creating over it would destroy that file and adopt its events.
            var directory = Path.Combine(_directory, RecordName.For(id));
            if (Directory.Exists(directory))
                throw new InvalidOperationException(
                    $"'{directory}' holds a thread for '{id}' that could not be read — repair or remove its {ThreadFile}");
            var thread = new Thread(new ChatThread(id, title, _clock(), metadata), directory);
            WriteThread(thread);
            threads[id] = thread;
            return Task.FromResult(thread.Value);
        }
    }

    public Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Threads().TryGetValue(id, out var t) ? t.Value : null);
    }

    public Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default) =>
        ListThreadsPageAsync(limit, null, ct);

    public Task<int> CountThreadsAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(Threads().Count);
    }

    public Task<IReadOnlyList<ChatThread>> ListThreadsPageAsync(int limit, ChatThread? after = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // created_at DESC, id DESC ordinal, strictly after the cursor — the order InMemory and SQLite share
            IEnumerable<ChatThread> q = Threads().Values.Select(t => t.Value)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id, StringComparer.Ordinal);
            if (after is not null)
                q = q.Where(t => t.CreatedAt < after.CreatedAt
                    || (t.CreatedAt == after.CreatedAt && string.CompareOrdinal(t.Id, after.Id) < 0));
            IReadOnlyList<ChatThread> page = [.. q.Take(limit)];
            return Task.FromResult(page);
        }
    }

    public Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!Threads().TryGetValue(id, out var t)) return Task.CompletedTask;
            var previous = t.Value;
            t.Value = previous with { Metadata = metadata };
            try { WriteThread(t); }
            catch { t.Value = previous; throw; }
        }
        return Task.CompletedTask;
    }

    public Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        lock (_lock)
        {
            // An event needs its thread, as the SQL backends' foreign key requires.
            if (!Threads().TryGetValue(threadId, out var t))
                throw new InvalidOperationException($"there is no thread '{threadId}' to append to");
            var messages = Messages(t);
            // MAX(seq)+1, never Count+1 — and a numbered file that did not load still holds its number.
            var seq = Math.Max(messages.Select(m => m.Seq).DefaultIfEmpty(0).Max(), FileSystemRoot.MaxId(t.Directory)) + 1;
            var message = new ChatMessage(Guid.NewGuid().ToString(), threadId, seq, kind, payload, metadata, _clock());
            root.Write(Path.Combine(t.Directory, FileSystemRoot.IdFile(seq)), RecordFile.Write(new RecordHeader()
                .Add("id", message.Id).Add("seq", seq).Add("kind", kind).Add("metadata", metadata)
                .Add("created", message.CreatedAt), payload));
            messages.Add(message);
            return Task.FromResult(message);
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ChatMessage> messages = Threads().TryGetValue(threadId, out var t) ? [.. Messages(t)] : [];
            return Task.FromResult(messages);
        }
    }

    public Task DeleteThreadAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Threads().TryGetValue(id, out var t))
            {
                FileSystemRoot.DeleteDirectory(t.Directory); // the directory first, as every delete here
                Threads().Remove(id);
            }
        }
        return Task.CompletedTask;
    }
}
