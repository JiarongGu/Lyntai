using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>A trigram index on <c>lyntai_memory_node.headline</c>, so recall can match an AUTHORED headline
/// without a sequential scan.
///
/// <para><b>Postgres-only.</b> A headline is either derived from the content or written by the caller
/// (<c>MemoryWrite.Headline</c>), and an authored one is a summary a consumer wrote so the entry could be
/// found by it — words that may appear nowhere in the content. SQLite's FTS5 mirror has indexed
/// <c>headline, content</c> since the graph store shipped, so it needs no counterpart; this index is what
/// lets Postgres match one too. <c>IMemoryGraphStore.SeedAsync</c>'s portable guarantee is a MINIMUM ("is
/// found on every backend"), not a ceiling, so the three backends are made to agree by WIDENING the two that
/// could not match a headline — never by confining the one that could.</para>
///
/// <para>No column is added and no data is rewritten: this is an index over a column
/// <c>M202608081215_MemoryGraph</c> already created as <c>TEXT NOT NULL</c>. It stays its OWN migration and
/// is never folded into the unreleased retention migration, whose schema goldens still match
/// unregenerated.</para></summary>
[Migration(202608152310)]
[Tags(nameof(StorageFeature.Memory), StorageFeatures.AllTag)]
public sealed class M202608152310_MemoryHeadlineSearch : Migration
{
    public override void Up() =>
        // Mirrors ix_lyntai_memory_node_trgm on `content`. A separate index rather than one over an
        // expression like (content || ' ' || headline): the store ORs two per-column predicates, so each
        // column needs its own index for either half to be served by one.
        Execute.Sql("""
            CREATE INDEX ix_lyntai_memory_node_headline_trgm
            ON lyntai_memory_node USING gin (headline gin_trgm_ops)
            """);

    public override void Down() =>
        Execute.Sql("DROP INDEX IF EXISTS ix_lyntai_memory_node_headline_trgm");
}
