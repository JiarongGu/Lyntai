using Lyntai.Lifecycle;

namespace Lyntai.Tests.Lifecycle;

/// <summary>What a backend DECLARES it can serve, checked before anything is spent.
///
/// <para>The generation domain had the right model in the wrong place: the LLM half of the library never
/// got a capability object at all, so "can this backend serve this request" was a type question there and a
/// data question here (<c>docs/DECISIONS.md</c> D125).</para></summary>
public class ProviderCapabilitiesTests
{
    private static ProviderCapabilities Text(params ProviderOperation[] operations) => new()
    {
        Kinds = [ProviderKinds.Text],
        Operations = operations,
    };

    [Fact]
    public void Serves_a_kind_and_operation_it_declares()
    {
        var capabilities = Text(ProviderOperation.Complete, ProviderOperation.Embed);

        Assert.True(capabilities.Supports(ProviderKinds.Text, ProviderOperation.Complete));
        Assert.True(capabilities.Supports(ProviderKinds.Text, ProviderOperation.Embed));
    }

    [Fact]
    public void Refuses_an_operation_it_does_not_declare_which_is_how_an_embedder_declines_chat()
    {
        // The whole point of capabilities as DATA: an embed-only backend answers "not mine" without
        // implementing a chat method that lies, and the router never dispatches to it.
        var embedOnly = Text(ProviderOperation.Embed);

        Assert.False(embedOnly.Supports(ProviderKinds.Text, ProviderOperation.Complete));
        Assert.False(embedOnly.Supports(ProviderKinds.Text, ProviderOperation.Stream));
    }

    [Fact]
    public void Refuses_a_kind_it_does_not_declare()
    {
        Assert.False(Text(ProviderOperation.Complete).Supports(ProviderKinds.Image, ProviderOperation.Complete));
    }

    [Fact]
    public void Matches_a_kind_CASE_INSENSITIVELY_because_a_kind_is_this_librarys_own_vocabulary()
    {
        Assert.True(Text(ProviderOperation.Complete).Supports("TEXT", ProviderOperation.Complete));
    }

    [Fact]
    public void An_EMPTY_declaration_serves_nothing_rather_than_everything()
    {
        // Fail CLOSED: a backend that forgot to declare must be skipped, not handed every request. The
        // default of a list is empty, so this is the value an unconfigured provider actually has.
        var silent = new ProviderCapabilities();

        Assert.False(silent.Supports(ProviderKinds.Text, ProviderOperation.Complete));
    }

    [Fact]
    public void An_empty_MODEL_list_means_ANY_model_which_is_what_an_aggregator_needs()
    {
        // Opposite default from Kinds/Operations, deliberately: an aggregator serves hundreds behind one id
        // and cannot enumerate them, so silence there means "no restriction" rather than "none".
        var any = Text(ProviderOperation.Complete);

        Assert.True(any.Supports(ProviderKinds.Text, ProviderOperation.Complete, "some-vendor-model-v3"));
    }

    [Fact]
    public void A_declared_model_list_PINS_which_models_the_backend_serves()
    {
        var pinned = Text(ProviderOperation.Complete) with { Models = ["gpt-4o", "gpt-4o-mini"] };

        Assert.True(pinned.Supports(ProviderKinds.Text, ProviderOperation.Complete, "GPT-4o"));
        Assert.False(pinned.Supports(ProviderKinds.Text, ProviderOperation.Complete, "claude-opus"));
        Assert.True(pinned.Supports(ProviderKinds.Text, ProviderOperation.Complete));
    }

    [Fact]
    public void Refuses_a_request_carrying_INPUTS_when_it_cannot_take_them()
    {
        // A backend that ignores request inputs while accepting the call produces a plausible, wrong result
        // — the exact defect found in ComfyUiProvider (docs/task-archive.md Part 125).
        var noInputs = Text(ProviderOperation.Complete);
        var withInputs = noInputs with { SupportsInputs = true };

        Assert.False(noInputs.Supports(ProviderKinds.Text, ProviderOperation.Complete, hasInputs: true));
        Assert.True(withInputs.Supports(ProviderKinds.Text, ProviderOperation.Complete, hasInputs: true));
    }
}
