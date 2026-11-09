using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary><c>:memory:</c> names a database that lives and dies with ONE connection, and Lyntai opens one per
/// operation plus the migrator's own: each would see a private empty database, so the schema the migrator built
/// is gone when a store looks for it. The wiring and the factory refuse it up front instead.</summary>
public sealed class SqliteInMemoryPathTests
{
    [Theory]
    [InlineData(":memory:")]
    [InlineData(":MEMORY:")]
    [InlineData("  :memory: ")]
    [InlineData("file::memory:")]
    [InlineData("file::memory:?cache=shared")]
    [InlineData("file:lyntai?mode=memory&cache=shared")]
    public void UseSqliteStorage_refuses_an_in_memory_database(string path)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("fake"))
            .UseSqliteStorage(path)));

        Assert.Equal("dbPath", ex.ParamName);
    }

    [Theory]
    [InlineData(":memory:")]
    [InlineData("file::memory:?cache=shared")]
    public void The_connection_factory_refuses_an_in_memory_database(string path)
    {
        var ex = Assert.Throws<ArgumentException>(() => new SqliteConnectionFactory(path));

        Assert.Equal("dbPath", ex.ParamName);
    }

    [Fact]
    public void An_ordinary_path_is_still_accepted()
    {
        using var db = new TempDbPath("not-memory");

        _ = new SqliteConnectionFactory(db.Path);
    }
}
