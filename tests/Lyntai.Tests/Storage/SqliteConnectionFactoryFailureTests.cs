using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>A connection whose pragma batch fails is DISPOSED before the exception leaves the factory, on
/// both open paths — a leaked one keeps the database file (and its <c>-wal</c>/<c>-shm</c>) open until a
/// finalizer runs.</summary>
public class SqliteConnectionFactoryFailureTests
{
    // The pragma batch is the first statement to read the header, so a file that is not a database opens
    // and then fails there.
    private static string NotADatabase(TempDbPath temp)
    {
        File.WriteAllText(temp.Path, new string('x', 4096));
        return temp.Path;
    }

    // A disposed connection sits in the pool, which ClearPool closes; a leaked one is still open, so on
    // Windows the delete fails with the file in use.
    private static void AssertReleased(string path)
    {
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            SqliteConnection.ClearPool(c);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Open_disposes_the_connection_when_the_pragmas_fail()
    {
        using var temp = new TempDbPath("notadb");
        var path = NotADatabase(temp);

        Assert.ThrowsAny<SqliteException>(() => new SqliteConnectionFactory(path).Open());

        AssertReleased(path);
    }

    [Fact]
    public async Task OpenAsync_disposes_the_connection_when_the_pragmas_fail()
    {
        using var temp = new TempDbPath("notadb");
        var path = NotADatabase(temp);

        await Assert.ThrowsAnyAsync<SqliteException>(() => new SqliteConnectionFactory(path).OpenAsync());

        AssertReleased(path);
    }
}
