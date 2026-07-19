using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Adds the ambient W3C trace id to a run trace — the join key between a persisted trace
/// and the distributed trace in an OTel backend.</summary>
[Migration(202607170008)]
[Tags(nameof(StorageFeature.Trace), StorageFeatures.AllTag)]
public sealed class M202607170008_TraceId : Migration
{
    public override void Up()
    {
        Execute.Sql("ALTER TABLE lyntai_run_trace ADD COLUMN trace_id TEXT NULL");
    }

    public override void Down()
    {
        // SQLite pre-3.35 can't DROP COLUMN; the nullable column is harmless if left.
    }
}
