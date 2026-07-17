using FluentMigrator.Runner.VersionTableInfo;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>The lyntai_-prefixed version table (a Postgres-package copy — adapters never reference
/// each other, so the metadata class is duplicated rather than shared with the SQLite package).</summary>
[VersionTableMetaData]
public sealed class LyntaiVersionTable : IVersionTableMetaData
{
    public string SchemaName => "";
    public string TableName => "lyntai_version_info";
    public string ColumnName => "version";
    public string DescriptionColumnName => "description";
    public string UniqueIndexName => "ux_lyntai_version_info";
    public string AppliedOnColumnName => "applied_on";
    public bool OwnsSchema => true;
    public bool CreateWithPrimaryKey => false;
}
