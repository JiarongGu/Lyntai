using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>The named factories exist because the positional constructor is a SILENT-misbinding trap: three of
/// <c>GenerationInput(string MediaType, byte[]? Data, string? Uri, string? Role)</c>'s four slots are strings
/// and <c>Role</c> is last, so <c>new GenerationInput(GenerationInputRoles.Init, bytes, "image/png")</c>
/// compiles clean, binds <c>"init"</c> to the media type and leaves the role NULL — an img2img request that
/// degrades to text-to-image with no error anywhere (TASKS.md GEN10, reported from a real consumer).
///
/// These tests pin the property the factories buy: the role is baked in by construction and cannot be
/// omitted.</summary>
public class GenerationInputFactoryTests
{
    private static readonly byte[] Pixels = [1, 2, 3];

    [Fact]
    public void Init_bakes_the_role_the_positional_call_silently_loses()
    {
        var input = GenerationInput.Init(Pixels, "image/png");

        Assert.Equal(GenerationInputRoles.Init, input.Role);
        Assert.Equal("image/png", input.MediaType);
        Assert.Same(Pixels, input.Data);
        Assert.Null(input.Uri);
    }

    [Theory]
    [InlineData(GenerationInputRoles.Init)]
    [InlineData(GenerationInputRoles.FirstFrame)]
    [InlineData(GenerationInputRoles.Reference)]
    [InlineData(GenerationInputRoles.Voice)]
    public void Every_well_known_role_has_a_factory(string role)
    {
        // one factory per GenerationInputRoles constant — a role a caller can name but not construct safely
        // would just push them back to the positional ctor
        var input = ByRole(role);

        Assert.Equal(role, input.Role);
        Assert.Same(Pixels, input.Data);
    }

    [Fact]
    public void A_uri_input_carries_the_location_and_no_bytes()
    {
        // System.Uri, not string, on purpose: with two strings the same media-type/uri transposition would be
        // back. A wrong type is a compile error; a wrong string is a silent one.
        var input = GenerationInput.Reference(new Uri("https://example.invalid/style.png"), "image/png");

        Assert.Equal(GenerationInputRoles.Reference, input.Role);
        Assert.Equal("https://example.invalid/style.png", input.Uri);
        Assert.Null(input.Data);
    }

    [Fact]
    public void From_carries_a_role_the_platform_does_not_know_because_roles_are_open_strings()
    {
        // GenerationInputRoles is constants, not an enum, so a backend may document its own role — the escape
        // hatch has to be as safe as the named four, which is why role comes FIRST here
        var input = GenerationInput.From("mask", Pixels, "image/png");

        Assert.Equal("mask", input.Role);
        Assert.Same(Pixels, input.Data);

        var byUri = GenerationInput.From("mask", new Uri("https://example.invalid/m.png"), "image/png");

        Assert.Equal("mask", byUri.Role);
        Assert.Equal("https://example.invalid/m.png", byUri.Uri);
    }

    [Fact]
    public void A_factory_refuses_what_the_backend_could_only_fail_on_later()
    {
        // an empty media type or a null payload reaches the backend as a malformed multipart part or an
        // unreadable input — loudly here beats mysteriously there
        Assert.Throws<ArgumentNullException>(() => GenerationInput.Init((byte[])null!, "image/png"));
        Assert.Throws<ArgumentException>(() => GenerationInput.Init(Pixels, "  "));
        Assert.Throws<ArgumentNullException>(() => GenerationInput.Voice((Uri)null!, "audio/wav"));
        Assert.Throws<ArgumentException>(() => GenerationInput.From(" ", Pixels, "image/png"));
    }

    [Fact]
    public void A_factory_input_round_trips_through_an_artifact_chain()
    {
        // ToInput is the OTHER way to build an input (pipeline chaining) and already takes the role explicitly;
        // the two paths must produce the same shape or a pipeline stage and a hand-built stage would differ
        var artifact = new GenerationArtifact("image/png", Pixels);

        Assert.Equal(GenerationInput.Init(Pixels, "image/png"), artifact.ToInput(GenerationInputRoles.Init));
    }

    private static GenerationInput ByRole(string role) => role switch
    {
        GenerationInputRoles.Init => GenerationInput.Init(Pixels, "image/png"),
        GenerationInputRoles.FirstFrame => GenerationInput.FirstFrame(Pixels, "image/png"),
        GenerationInputRoles.Reference => GenerationInput.Reference(Pixels, "image/png"),
        GenerationInputRoles.Voice => GenerationInput.Voice(Pixels, "audio/wav"),
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
