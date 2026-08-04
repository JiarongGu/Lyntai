using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>Capability declaration is what makes media routing possible at all: unlike chat models (which all
/// take text), media backends differ in medium, delivery mode, inputs and limits. <c>Supports</c> is the
/// pre-filter the router uses to skip a candidate that CANNOT serve a request, instead of failing it.</summary>
public class GenerationCapabilitiesTests
{
    private static readonly GenerationCapabilities ImageOnly = new()
    {
        Kinds = [GenerationKinds.Image],
        Deliveries = [GenerationDelivery.Inline],
        SupportsInputs = true,
    };

    [Fact]
    public void A_backend_supports_a_request_for_a_kind_it_advertises()
    {
        Assert.True(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image }, GenerationDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_medium_it_never_claimed()
    {
        Assert.False(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Video }, GenerationDelivery.Inline));
    }

    [Fact]
    public void A_backend_does_not_support_a_delivery_mode_it_lacks()
    {
        // asking an inline-only backend to stream is a capability gap, not a failure
        Assert.False(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image }, GenerationDelivery.Stream));
    }

    [Fact]
    public void A_backend_that_takes_no_inputs_does_not_support_an_input_carrying_request()
    {
        var textOnly = new GenerationCapabilities
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
            SupportsInputs = false,
        };
        var imageToVideo = new GenerationRequest
        {
            Kind = GenerationKinds.Video,
            Inputs = [new GenerationInput("image/png", Data: [1], Role: GenerationInputRoles.FirstFrame)],
        };

        Assert.False(textOnly.Supports(imageToVideo, GenerationDelivery.Job));
    }

    [Fact]
    public void A_model_the_backend_does_not_serve_is_unsupported()
    {
        // an aggregator serves hundreds of models; a request naming one it doesn't have must be skipped, not sent
        var aggregator = new GenerationCapabilities
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
            Models = ["wan-t2v", "kling-v2"],
        };

        Assert.True(aggregator.Supports(new GenerationRequest { Kind = GenerationKinds.Video, Model = "wan-t2v" }, GenerationDelivery.Job));
        Assert.False(aggregator.Supports(new GenerationRequest { Kind = GenerationKinds.Video, Model = "nope" }, GenerationDelivery.Job));
    }

    [Fact]
    public void An_empty_model_list_means_the_backend_does_not_enumerate_them()
    {
        // a single-model backend (or one whose catalogue we don't mirror) must not be filtered out by a
        // model it never listed — absence of a list is "unknown", not "supports nothing"
        Assert.True(ImageOnly.Supports(new GenerationRequest { Kind = GenerationKinds.Image, Model = "whatever" }, GenerationDelivery.Inline));
    }
}
