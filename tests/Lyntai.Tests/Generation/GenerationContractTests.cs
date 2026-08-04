using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>The vocabulary every media backend speaks. These assertions pin the DEFAULTS, because a
/// half-initialised request is the difference between "the backend refused" and "we sent nonsense".</summary>
public class GenerationContractTests
{
    [Fact]
    public void A_request_defaults_to_no_inputs_and_no_options()
    {
        var request = new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "a red square" };

        Assert.Empty(request.Inputs);
        Assert.Empty(request.Options);
        Assert.Null(request.Model);
    }

    [Fact]
    public void An_input_carries_bytes_or_a_uri_and_a_free_form_role()
    {
        // WAN alone has text→video, image→video (FIRST FRAME) and reference→video: the role is what
        // distinguishes them, and it is free-form so another backend's roles fit without a contract change
        var input = new GenerationInput("image/png", Data: [1, 2, 3], Role: GenerationInputRoles.FirstFrame);

        Assert.Equal("image/png", input.MediaType);
        Assert.Equal("first-frame", input.Role);
        Assert.Null(input.Uri);
    }

    [Fact]
    public void A_failed_result_is_not_ok_and_carries_no_artifacts()
    {
        var result = GenerationResult.Failure(GenerationVerdict.NotConfigured, "no endpoint configured");

        Assert.False(result.IsOk);
        Assert.Empty(result.Artifacts);
        Assert.Equal(GenerationVerdict.NotConfigured, result.Verdict);
        Assert.Contains("no endpoint", result.Detail);
    }

    [Fact]
    public void An_ok_result_requires_at_least_one_artifact()
    {
        // an "Ok" with nothing in it is the empty-Ok mistake the LLM side already learned (pitfalls.md):
        // the router must be able to fall over instead of handing back a successful nothing
        var ex = Assert.Throws<ArgumentException>(() => GenerationResult.Success([]));

        Assert.Contains("artifact", ex.Message);
    }

    [Fact]
    public void Well_known_kinds_are_open_strings_not_an_enum()
    {
        // 3D already exists on real aggregators; the next medium must not be a breaking change
        Assert.Equal("image", GenerationKinds.Image);
        Assert.Equal("video", GenerationKinds.Video);
        Assert.Equal("audio", GenerationKinds.Audio);
        Assert.Equal("3d", GenerationKinds.Model3d);
    }

    [Fact]
    public void An_artifact_becomes_the_next_stage_s_input()
    {
        // CHAINING is a first-class use case: 3d → image → video, or image → video-first-frame. One stage's
        // output must feed the next without the caller re-wrapping bytes by hand.
        var rendered = new GenerationArtifact("image/png", Data: [1, 2, 3],
            Metadata: new Dictionary<string, string> { ["seed"] = "42" });

        var input = rendered.ToInput(GenerationInputRoles.FirstFrame);

        Assert.Equal("image/png", input.MediaType);
        Assert.Equal([1, 2, 3], input.Data);
        Assert.Equal("first-frame", input.Role);
    }

    [Fact]
    public void An_artifact_returned_as_a_uri_chains_as_a_uri()
    {
        // video backends commonly return a signed URL; chaining must not force a download the caller
        // didn't ask for (the next backend may well be able to read the URL itself)
        var hosted = new GenerationArtifact("video/mp4", Uri: "https://example.invalid/a.mp4");

        var input = hosted.ToInput(GenerationInputRoles.Reference);

        Assert.Null(input.Data);
        Assert.Equal("https://example.invalid/a.mp4", input.Uri);
    }
}
