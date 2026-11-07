using System.Globalization;
using System.Text.Json;
using Lyntai.Providers.Acp;
using Lyntai.Providers.Basic;

namespace Lyntai.Tests.Providers;

/// <summary>The bounds and the spelling of what the ACP door's readers keep and state.</summary>
public class AcpReaderBoundsTests
{
    [Fact]
    public void An_unread_kind_is_cut_whole_however_long_the_agent_names_it()
    {
        var unread = new UnreadFrames();

        unread.Note("session/update", new string('k', 1_000_000), new string('x', 1_000_000));

        var kept = Assert.Single(unread.Snapshot());
        Assert.True(kept.Subtype!.Length <= UnreadFrames.MaxKind);
        Assert.True(kept.Sample.Length <= UnreadFrames.MaxSample);
    }

    [Fact]
    public void Two_kinds_that_differ_only_past_the_cut_count_as_one()
    {
        var unread = new UnreadFrames();
        var head = new string('k', UnreadFrames.MaxKind);

        unread.Note("session/update", head + "a", "{}");
        unread.Note("session/update", head + "b", "{}");

        Assert.Equal(2, Assert.Single(unread.Snapshot()).Count);
    }

    [Fact]
    public void The_auth_code_is_spelled_the_same_in_every_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");   // its minus sign is U+2212
            using var error = JsonDocument.Parse("""{"code":-32000,"message":"sign in"}""");

            var failure = AcpFailure.FromError(error.RootElement, new AcpExtensionSet([], TimeProvider.System), TimeProvider.System, out _);

            Assert.Equal("acp.error.code=-32000", failure.By);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
