using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="ConversationStoreContract"/> fact as a theory case — derive with a store factory
/// and the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).</summary>
public abstract class ConversationStoreContractFacts : StoreContractSuite<IConversationStore>
{
    protected abstract IConversationStore NewStore();
    protected sealed override IConversationStore CreateStore(string fact) => NewStore();

    [SkippableTheory, ContractFacts(typeof(ConversationStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(ConversationStoreContract), fact);
}

/// <summary>The <see cref="ConversationStoreContract"/> against the InMemory backend.</summary>
public class InMemoryConversationStoreContractTests : ConversationStoreContractFacts
{
    protected override IConversationStore NewStore() => new InMemoryConversationStore();
}

/// <summary>The <see cref="ConversationStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteConversationStoreContractTests : ConversationStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    protected override IConversationStore NewStore() => new SqliteConversationStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}
