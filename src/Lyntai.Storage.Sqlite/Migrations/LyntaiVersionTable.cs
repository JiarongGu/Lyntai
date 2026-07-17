using FluentMigrator.Runner.VersionTableInfo;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>FluentMigrator's default version table is the very generic "VersionInfo" — in a shared
/// consumer database that's the likeliest collision of all, so it gets the lyntai_ prefix too.</summary>
[VersionTableMetaData]
public sealed class LyntaiVersionTable : IVersionTableMetaData
{
    public string SchemaName => "";
    public string TableName => "lyntai_version_info";
    public string ColumnName => "Version";
    public string DescriptionColumnName => "Description";
    public string UniqueIndexName => "ux_lyntai_version_info";
    public string AppliedOnColumnName => "AppliedOn";
    public bool OwnsSchema => true;
    public bool CreateWithPrimaryKey => false;
}
