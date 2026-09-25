using System.Security.Cryptography;
using Lyntai.Secrets;
using Lyntai.Tests.Fakes;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Secrets;

/// <summary><see cref="KeyValueSecretVault.ListNamesAsync"/> reads the names from the stored keys themselves,
/// so no second write can drift from them — a secret that is stored is a secret that is listed.</summary>
public sealed class KeyValueSecretVaultListingTests
{
    [Fact]
    public async Task A_stored_secret_is_listed_even_when_no_index_write_accompanied_it()
    {
        var kv = new InMemoryKeyValueStore();
        var protector = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
        // what a racing writer, or a Set whose second write failed, leaves behind: the value and nothing else
        await kv.SetAsync("lyntai:secret:orphan", protector.Protect("v"));

        var vault = new KeyValueSecretVault(kv, protector);

        Assert.Equal(["orphan"], await vault.ListNamesAsync());
        Assert.Equal("v", await vault.GetAsync("orphan"));
    }

    [Fact]
    public async Task Names_list_in_ordinal_order_and_keep_their_own_colons()
    {
        var kv = new InMemoryKeyValueStore();
        var vault = new KeyValueSecretVault(kv, new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));

        await vault.SetAsync("b", "1");
        await vault.SetAsync("a:nested", "2");

        Assert.Equal(["a:nested", "b"], await vault.ListNamesAsync());
    }
}
