using System.Data;
using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A vector as a blob beside its JSON text, so a search reads four bytes a component instead of parsing
/// them (<see cref="VectorBlob"/> is the layout).
/// <para><b>The column is NULLABLE, and the migration fills it for every row it can</b>: SQL cannot turn JSON text
/// into floats, so the backfill walks the table in C# with the same parse the scan used, a page at a time. A row
/// whose text is not a vector keeps NULL, and a NULL row is read from its text, which is also what a row written
/// by a connection still on the old schema looks like. The text column stays and is still written, so an older
/// build reading a newer database scores what it always scored.</para>
/// <para>The Postgres store has no twin: it keeps pgvector's own column and creates its table outside the
/// migrations.</para></summary>
[Migration(202610281422)]
[Tags(nameof(StorageFeature.Governance), StorageFeatures.AllTag)]
public sealed class M202610281422_VectorBlob : Migration
{
    private const int Page = 500;

    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_vector ADD COLUMN vector_blob BLOB NULL");
        Execute.WithConnection(Backfill);
    }

    public override void Down()
    {
        Execute.Sql("ALTER TABLE lyntai_vector DROP COLUMN vector_blob");
    }

    private static void Backfill(IDbConnection connection, IDbTransaction? transaction)
    {
        string? collection = null, id = null;
        while (true)
        {
            var page = new List<(string Collection, string Id, byte[] Blob)>(Page);
            var last = (Collection: collection, Id: id);
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = """
                    SELECT collection, vec_id, vector FROM lyntai_vector
                    WHERE (@collection IS NULL OR (collection, vec_id) > (@collection, @id))
                    ORDER BY collection, vec_id LIMIT @page
                    """;
                Add(read, "@collection", collection);
                Add(read, "@id", id);
                Add(read, "@page", Page);
                using var reader = read.ExecuteReader();
                while (reader.Read())
                {
                    last = (reader.GetString(0), reader.GetString(1));
                    if (VectorBlob.TryFromJson(reader.GetString(2), out var vector))
                        page.Add((last.Collection, last.Id!, VectorBlob.Encode(vector)));
                }
            }

            if (last.Collection is null || last == (collection, id)) return;
            foreach (var (c, i, blob) in page)
            {
                using var write = connection.CreateCommand();
                write.Transaction = transaction;
                write.CommandText = "UPDATE lyntai_vector SET vector_blob = @blob WHERE collection = @collection AND vec_id = @id";
                Add(write, "@blob", blob);
                Add(write, "@collection", c);
                Add(write, "@id", i);
                write.ExecuteNonQuery();
            }

            (collection, id) = last;
        }
    }

    private static void Add(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
