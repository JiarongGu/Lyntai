using System.Reflection;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

public class ProviderIdentityTests
{
    // The declarations that look redundant next to IProviderIdentity.Id are the BINARY compatibility of the
    // frozen 1.0 surface, and nothing else in the build says so. Adding a base interface is binary-safe;
    // deleting the member from the derived interface is not — a consumer assembly compiled against 1.0 emits
    // `callvirt IModelProvider::get_Id`, and member resolution does not walk base INTERFACES, so that call
    // throws MissingMethodException until the consumer is recompiled. `consumer-smoke` cannot catch it: it
    // rebuilds the consumer from source every time, which is precisely the step a package upgrade skips.
    // Without this test the next "remove the duplicate declaration" cleanup passes every gate we have.
    [Theory]
    [InlineData(typeof(IModelProvider))]
    public void Every_seam_still_declares_Id_itself(Type seam)
    {
        var declared = seam.GetProperty(
            "Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(declared);
        Assert.Equal(typeof(string), declared.PropertyType);
        Assert.NotNull(declared.GetMethod);   // the get_Id slot old IL binds to
    }

    [Fact]
    public void Every_provider_seam_is_a_provider_identity()
    {
        Assert.True(typeof(IProviderIdentity).IsAssignableFrom(typeof(IModelProvider)));
    }

}
