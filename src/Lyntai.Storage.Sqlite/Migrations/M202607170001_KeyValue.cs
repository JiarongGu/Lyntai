using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

// All Lyntai tables carry the lyntai_ prefix: the storage package may be pointed at a consumer's
// existing database and must never collide with its tables.
[Migration(202607170001)]
public sealed class M202607170001_KeyValue : Migration
{
    public override void Up()
    {
        Create.Table("lyntai_kv")
            .WithColumn("key").AsString().PrimaryKey()
            .WithColumn("value").AsString().NotNullable()
            .WithColumn("updated_at").AsString().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("lyntai_kv");
    }
}
