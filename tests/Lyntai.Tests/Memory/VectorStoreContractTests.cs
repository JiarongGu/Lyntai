using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="VectorStoreContract"/> fact against the in-process store; SQLite derives beside its
/// fixture (<c>SqliteVectorStoreContractTests</c>), and Postgres wires each fact by name
/// (<c>PostgresGovernanceStoreTests</c>).</summary>
public class InMemoryVectorStoreContractTests : VectorStoreContractFacts
{
    protected override IVectorStore New() => new InMemoryVectorStore();
}
