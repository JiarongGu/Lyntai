using FluentMigrator;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A job stage's code and arguments (<c>stage_detail</c>): the <c>{"code","args"}</c> a coded
/// <c>JobMessage</c> carries beside its text, which stays in <c>stage</c> so every string reader is unchanged. Null
/// for a plain stage — and for every row written before this, which therefore reads as a plain message.</summary>
[Migration(202609262244)]
[Tags(nameof(StorageFeature.Jobs), StorageFeatures.AllTag)]
public sealed class M202609262244_JobStageDetail : Migration
{
    public override void Up() => Execute.Sql("ALTER TABLE lyntai_job ADD COLUMN stage_detail TEXT NULL");

    public override void Down() => Execute.Sql("ALTER TABLE lyntai_job DROP COLUMN stage_detail");
}
