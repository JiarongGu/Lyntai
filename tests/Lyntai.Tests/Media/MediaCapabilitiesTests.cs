using Lyntai.Media;

namespace Lyntai.Tests.Media;

/// <summary>Capability declaration is what makes media routing possible at all: unlike chat models (which all
/// take text), media backends differ in medium, delivery mode, inputs and limits. <c>Supports</c> is the
/// pre-filter the router uses to skip a candidate that CANNOT serve a request, instead of failing it.</summary>
public class MediaCapabilitiesTests
{
    private static readonly MediaCapabilities ImageOnly = new()
    {
        Kinds = [MediaKinds.Image],
        Deliveries = [MediaDelivery.Inline],
        SupportsInputs = true,
    };

    [Fact]
    public void A_backend_supports_a_request_for_a_kind_it_advertises()
    {
        Assert.True(ImageOnly.Supports(new MediaRequest { Kind = MediaKinds.Image }, MediaDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_medium_it_never_claimed()
    {
        Assert.False(ImageOnly.Supports(new MediaRequest { Kind = MediaKinds.Video }, MediaDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_delivery_mode_it_lacks()
    {
        // asking an inline-only backend to stream is a capability gap, not a failure
        Assert.False(ImageOnly.Supports(new MediaRequest { Kind = MediaKinds.Image }, MediaDelivery.Stream));
    }

    [Fact]
    public void A_backend_that_takes_no_inputs_does_not_support_an_input_carrying_request()
    {
        var textOnly = new MediaCapabilities
        {
            Kinds = [MediaKinds.Video],
            Deliveries = [MediaDelivery.Job],
            SupportsInputs = false,
        };
        var imageToVideo = new MediaRequest
        {
            Kind = MediaKinds.Video,
            Inputs = [new MediaInput("image/png", Data: [1], Role: MediaInputRoles.FirstFrame)],
        };

        Assert.False(textOnly.Supports(imageToVideo, MediaDelivery.Job));
    }

    [Fact]
    public void A_model_the_backend_does_not_serve_is_unsupported()
    {
        // an aggregator serves hundreds of models; a request naming one it doesn't have must be skipped, not sent
        var aggregator = new MediaCapabilities
        {
            Kinds = [MediaKinds.Video],
            Deliveries = [MediaDelivery.Job],
            Models = ["wan-t2v", "kling-v2"],
        };

        Assert.True(aggregator.Supports(new MediaRequest { Kind = MediaKinds.Video, Model = "wan-t2v" }, MediaDelivery.Job));
        Assert.False(aggregator.Supports(new MediaRequest { Kind = MediaKinds.Video, Model = "nope" }, MediaDelivery.Job));
    }

    [Fact]
    public void An_empty_model_list_means_the_backend_does_not_enumerate_them()
    {
        // a single-model backend (or one whose catalogue we don't mirror) must not be filtered out by a
        // model it never listed — absence of a list is "unknown", not "supports nothing"
        Assert.True(ImageOnly.Supports(new MediaRequest { Kind = MediaKinds.Image, Model = "whatever" }, MediaDelivery.Inline));
    }
}
