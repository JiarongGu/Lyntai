using FluentMigrator;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>A trigram index on <c>lyntai_memory_node.headline</c>, so recall can match an AUTHORED headline
/// without a sequential scan.
///
/// <para><b>Why this exists, and why it is Postgres-only.</b> A headline is either derived from the content
/// or written by the caller (<c>MemoryWrite.Headline</c>), and an authored one is a summary a consumer wrote
/// specifically so the entry could be found by it — words that may appear nowhere in the content. SQLite's
/// FTS5 mirror has indexed <c>headline, content</c> since the graph store shipped, so SQLite could always
/// match one; Postgres indexed <c>content</c> alone and the in-process store read <c>Content</c> alone, so
/// neither could. Same call, same data, different answers — the divergence class <c>storage.md</c> calls a
/// defect rather than a difference.</para>
///
/// <para><b>The 3.0 review first closed that gap the other way</b>, by confining SQLite's FTS expression to
/// <c>content</c>, on the reading that <c>IMemoryGraphStore.SeedAsync</c>'s portable guarantee is written
/// content-only. Round 2 of the same review corrected it: that guarantee states a MINIMUM ("is found on
/// every backend"), not a ceiling, so SQLite was not exceeding a contract — and narrowing removed a
/// capability from the one backend that had it, with no measurement and nothing in the migration guide.
/// Widening is the direction that makes the three agree without taking anything away.</para>
///
/// <para>No column is added and no data is rewritten: this is an index over a column
/// <c>M202608081215_MemoryGraph</c> already created as <c>TEXT NOT NULL</c>. Kept as its OWN migration
/// rather than folded into the unreleased retention migration, because that one's schema goldens were
/// captured pre-squash and still match unregenerated — which is the PROOF the fold was equivalent, and
/// editing it would spend that proof for an unrelated change.</para></summary>
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
