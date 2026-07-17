using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

[Migration(202607170001)]
public sealed class M202607170001_KeyValue : Migration
{
    public override void Up()
    {
        Create.Table("app_config")
            .WithColumn("key").AsString().PrimaryKey()
            .WithColumn("value").AsString().NotNullable()
            .WithColumn("updated_at").AsString().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("app_config");
    }
}
