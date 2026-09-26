using Lyntai.Storage.Sqlite.Migrations;
using Lyntai.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>The app-owned-schema recipe — <c>MigrateUp(path)</c> before <c>SchemaMigration.None</c> — works
/// on a path whose directory does not exist yet, exactly as <c>UseSqliteStorage(path)</c> does.</summary>
public sealed class MigrateUpDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(TestPaths.TestDbsDir, $"nested-{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_root, "deeper", "lyntai.db");

    public void Dispose()
    {
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString()))
            SqliteConnection.ClearPool(c);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* gitignored scratch */ }
    }

    [Fact]
    public void MigrateUp_creates_the_directory_it_needs()
    {
        MigrationRunnerService.MigrateUp(DbPath);

        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public async Task MigrateUpAsync_creates_the_directory_it_needs()
    {
        await MigrationRunnerService.MigrateUpAsync(DbPath);

        Assert.True(File.Exists(DbPath));
    }
}
