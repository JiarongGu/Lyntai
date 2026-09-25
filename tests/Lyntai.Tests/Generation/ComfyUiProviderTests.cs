using Lyntai.Inference;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The local ComfyUI backend — the GRAPH-shaped one, and the platform's only ASYNC-JOB backend so
/// far. It matters for three reasons the other two don't cover: the caller supplies a workflow rather than a
/// bare prompt (so <c>Prompt</c> may be null and <c>Options</c> carries the graph), delivery is
/// submit → poll → fetch on a LOCAL server, and it is the candidate a host pairs with a hosted one when a
/// refusal should be picked up locally.
///
/// The endpoint paths and response fields are MEASURED — <see cref="ComfyUiLiveTests"/> ran the whole
/// queued surface against a real ComfyUI (0.36.0) and every documented default answered as shipped. Each
/// stays a settable option (see <see cref="ComfyUiOptions"/>) because upstream can move. These tests pin
/// the CONTRACT (what the provider does with what it gets); the live suite is what claims the real server
/// speaks it.</summary>
public class ComfyUiProviderTests
{
    private const string Workflow = """{"3":{"class_type":"KSampler","inputs":{"seed":0}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":"placeholder"}}}""";

    private static (ComfyUiProvider Provider, StubHttpHandler Http) Provider(ComfyUiOptions? options = null)
    {
        var handler = new StubHttpHandler();
        var provider = new ComfyUiProvider(
            options ?? new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(handler, disposeHandler: false));
        return (provider, handler);
    }

    private static MediaRequest Ask(string? prompt = "a red square", string? workflow = Workflow)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (workflow is not null) options["workflow"] = workflow;
        options["prompt-path"] = "6.inputs.text";
        return new MediaRequest { Kind = ProviderKinds.Image, Prompt = prompt, Options = options };
    }

    [Fact]
    public void It_declares_itself_a_JOB_backend_across_image_video_and_3d()
    {
        var (provider, _) = Provider();

        Assert.Equal("comfyui", provider.Id);
        Assert.Contains(ProviderKinds.Image, provider.Capabilities.Produces);
        Assert.Contains(ProviderKinds.Video, provider.Capabilities.Produces);   // local video via a workflow
        Assert.Contains(ProviderKinds.Model3d, provider.Capabilities.Produces); // a mesh via a workflow
        Assert.Equal([ProviderOperation.Queued], provider.Capabilities.Operations);
        Assert.IsAssignableFrom<IMediaJobProvider>(provider);
    }

    [Fact]
    public void It_declares_SupportsInputs_because_every_input_is_bound_or_refused()
    {
        // SupportsInputs is an ADMISSION filter, so it is a promise that MediaRequest.Inputs is read: each is
        // uploaded to the field the caller names, and one with no field is refused (the facts below)
        var (provider, _) = Provider();

        Assert.True(provider.Capabilities.SupportsInputs);
        Assert.True(provider.Capabilities.Supports(
            ProviderKinds.Model3d, ProviderOperation.Queued, ProviderKinds.Text, hasInputs: true));
    }

    // ---- inputs: uploaded, then bound at the dotted path the caller names --------------------------------

    private const string MeshWorkflow = """
        {"1":{"class_type":"Load3DAdvanced","inputs":{"model_file":"none","viewport_state":{}}},
         "2":{"class_type":"LoadImage","inputs":{"image":"none"}},
         "3":{"class_type":"SaveGLB","inputs":{"mesh":["1",0]}}}
        """;

    private static MediaRequest MeshAsk(Dictionary<string, string> paths, params MediaInput[] inputs)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["workflow"] = MeshWorkflow };
        foreach (var (key, path) in paths) options[key] = path;
        return new MediaRequest { Kind = ProviderKinds.Model3d, Inputs = inputs, Options = options };
    }

    private static Dictionary<string, string> Bound(string key, string path) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = path };

    private static MediaInput Mesh(string content) =>
        new("model/gltf-binary", Data: Encoding.ASCII.GetBytes(content));

    private static string Uploaded(string name, string subfolder) =>
        $$"""{"name":"{{name}}","subfolder":"{{subfolder}}","type":"input"}""";

    private static string? Posted(string promptBody, string node, string field) =>
        JsonNode.Parse(promptBody)!["prompt"]![node]!["inputs"]![field]!.GetValue<string>();

    /// <summary>One field of a multipart body, or null. The fixtures carry no <c>--</c> of their own, so
    /// splitting on it is splitting on the boundary.</summary>
    private static string? FormField(string multipart, string field)
    {
        foreach (var part in multipart.Split("--"))
        {
            var split = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split < 0) continue;
            if (!Regex.IsMatch(part[..split],
                    $@"name=""?{Regex.Escape(field)}""?(;|\r|$)")) continue;
            return part[(split + 4)..].TrimEnd('\r', '\n');
        }
        return null;
    }

    private static string? UploadedFileName(string multipart) =>
        Regex.Match(multipart, @"filename=""?([^"";\r\n]+)") is { Success: true } m
            ? m.Groups[1].Value
            : null;

    [Fact]
    public async Task A_mesh_input_is_uploaded_into_the_3d_subfolder_and_the_RETURNED_name_is_written_at_its_path()
    {
        // the server may rename an upload, so the name written into the graph is the one it answered with
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, Uploaded("cube (1).glb", "3d"))
            .Enqueue(HttpStatusCode.OK, """{"prompt_id":"abc-123"}""");

        var operation = await provider.SubmitAsync(
            MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("MESH-BYTES-7F3A")));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/upload/image", http.Requests[0].Uri?.ToString());
        Assert.Equal("MESH-BYTES-7F3A", FormField(http.Requests[0].Body, "image"));
        Assert.Equal("3d", FormField(http.Requests[0].Body, "subfolder"));
        Assert.Equal("input", FormField(http.Requests[0].Body, "type"));
        Assert.EndsWith(".glb", UploadedFileName(http.Requests[0].Body));   // a loader picks its parser by it

        Assert.Equal("http://127.0.0.1:8188/prompt", http.Requests[1].Uri?.ToString());
        Assert.Equal("3d/cube (1).glb", Posted(http.Requests[1].Body, "1", "model_file"));
    }

    [Fact]
    public async Task A_URI_input_is_fetched_then_uploaded()
    {
        // a chained artifact arrives as a view URI; the loader reads the server's input folder, not a URL
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "MESH-FROM-URI", "application/octet-stream")
            .Enqueue(HttpStatusCode.OK, Uploaded("m.glb", "3d"))
            .Enqueue(HttpStatusCode.OK, """{"prompt_id":"abc-123"}""");
        var source = "http://127.0.0.1:8188/view?filename=a.glb&subfolder=3d&type=output";

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"),
            new MediaInput("model/gltf-binary", Uri: source)));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Equal(source, http.Requests[0].Uri?.ToString());
        Assert.Equal("MESH-FROM-URI", FormField(http.Requests[1].Body, "image"));
        Assert.Equal("3d/m.glb", Posted(http.Requests[2].Body, "1", "model_file"));
    }

    [Fact]
    public async Task An_input_WITH_a_role_binds_through_its_role_keyed_path_and_a_non_mesh_goes_to_the_input_root()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, Uploaded("m.glb", "3d"))
            .Enqueue(HttpStatusCode.OK, Uploaded("i.png", ""))
            .Enqueue(HttpStatusCode.OK, """{"prompt_id":"abc-123"}""");
        var paths = Bound("input-path", "1.inputs.model_file");
        paths["input-path:init"] = "2.inputs.image";

        var operation = await provider.SubmitAsync(MeshAsk(paths,
            Mesh("MESH"), MediaInput.Init(Encoding.ASCII.GetBytes("IMAGE"), "image/png")));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.True(string.IsNullOrEmpty(FormField(http.Requests[1].Body, "subfolder")));
        Assert.Equal("3d/m.glb", Posted(http.Requests[2].Body, "1", "model_file"));
        Assert.Equal("i.png", Posted(http.Requests[2].Body, "2", "image"));
    }

    [Fact]
    public async Task An_input_with_no_path_to_go_to_is_refused_and_nothing_is_uploaded_or_submitted()
    {
        // dropping it would run the graph as authored — billed, plausible, and not what was asked for
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask() with
        {
            Inputs = [MediaInput.FirstFrame(new byte[] { 1, 2, 3 }, "image/png")],
        });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("input-path:first-frame", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);   // the request, not the host
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_role_with_no_path_of_its_own_is_refused_rather_than_taking_the_roleless_one()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"),
            MediaInput.Init(new byte[] { 1 }, "image/png")));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("input-path:init", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_path_that_is_not_a_field_of_the_graph_is_refused()
    {
        // the prompt's path is left alone when it misses; an input's cannot be, or the input is dropped
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(
            MeshAsk(Bound("input-path", "9.inputs.model_file"), Mesh("MESH")));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("9.inputs.model_file", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Two_inputs_bound_to_one_field_are_refused_because_the_second_would_erase_the_first()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(
            MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("A"), Mesh("B")));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("1.inputs.model_file", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_failed_upload_is_a_failed_submit_with_the_servers_reason_and_the_workflow_is_never_sent()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, """{"error":"disk full"}""");

        var operation = await provider.SubmitAsync(
            MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("MESH")));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("disk full", operation.Detail);
        Assert.False(operation.Inconclusive);   // nothing was queued, so nothing can have been billed
        Assert.Null(operation.Verdict);         // a failing server IS a host fault: classified, and it counts
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task A_URI_that_cannot_be_fetched_is_a_failed_submit_naming_it()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.NotFound, "gone");
        var source = "http://127.0.0.1:8188/view?filename=a.glb&subfolder=3d&type=output";

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"),
            new MediaInput("model/gltf-binary", Uri: source)));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains(source, operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);   // a stale view URI is the request's fault
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task A_5xx_from_ComfyUIs_own_origin_while_fetching_is_left_a_host_fault()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "boom");

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"),
            new MediaInput("model/gltf-binary", Uri: "http://127.0.0.1:8188/view?filename=a.glb&type=output")));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Failed, EffectiveVerdict(operation));
    }

    [Fact]
    public async Task An_upload_the_server_refuses_with_a_4xx_is_Unsupported()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.RequestEntityTooLarge, "too large");

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("M")));

        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Single(http.Requests);
    }

    /// <summary>What <c>MediaRouter.SubmitAsync</c> acts on: the operation's own verdict, else its detail
    /// classified.</summary>
    private static ProviderVerdict EffectiveVerdict(QueuedOperation operation) =>
        operation.Verdict ?? ProviderVerdictClassifier.FromErrorText(operation.Detail);

    // ---- the queue call: a graph ComfyUI rejects is the request's fault, not the server's ----------------

    /// <summary>What <c>POST /prompt</c> answers when a graph fails validation — a missing model, a missing
    /// custom node, a value out of range.</summary>
    private const string ValidationFailure = """
        {"error":{"type":"prompt_outputs_failed_validation","message":"Prompt outputs failed validation",
          "details":"","extra_info":{}},
         "node_errors":{"4":{"errors":[{"type":"value_not_in_list","message":"Value not in list"}],
          "dependent_outputs":["9"],"class_type":"CheckpointLoaderSimple"}}}
        """;

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ProviderVerdict.Unsupported)]
    [InlineData(HttpStatusCode.NotFound, ProviderVerdict.Unsupported)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderVerdict.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderVerdict.NotConfigured)]
    [InlineData(HttpStatusCode.Forbidden, ProviderVerdict.NotConfigured)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderVerdict.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderVerdict.Failed)]
    public async Task A_refused_queue_call_is_classified_by_its_status(HttpStatusCode status, ProviderVerdict expected)
    {
        // a 4xx that is not about access or rate is ComfyUI refusing THIS graph; a 5xx is the server's own fault
        var (provider, http) = Provider();
        http.Enqueue(status, ValidationFailure);

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.False(operation.Inconclusive);   // it answered
        Assert.Equal(expected, EffectiveVerdict(operation));
    }

    [Fact]
    public async Task A_graph_that_fails_validation_advances_to_the_next_candidate_and_never_benches_ComfyUI()
    {
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var (comfy, http) = Provider();
        http.Enqueue(HttpStatusCode.BadRequest, ValidationFailure);   // repeats for every submit
        var other = new FakeGenerationJobProvider
        {
            Id = "other",
            Capabilities = new()
            {
                Accepts = [ProviderKinds.Text], Produces = [ProviderKinds.Image], Operations = [ProviderOperation.Queued],
            },
        };
        var router = new MediaRouter([comfy, other], deadHosts: tracker);

        for (var i = 0; i < 3; i++)
            Assert.Equal("other", (await router.SubmitAsync(
                [new ProviderCandidate("comfyui"), new ProviderCandidate("other")], Ask())).ProviderId);

        Assert.Equal(3, http.Requests.Count);    // asked every time: no strike, no bench
        Assert.False(tracker.IsDead("generation::comfyui"));
        Assert.Contains("failed validation", (await comfy.SubmitAsync(Ask())).Detail);
    }

    // ---- a URI input: fetched from any server, but ComfyUI's credentials stay on ComfyUI's origin --------

    private const string Secret = "comfy-secret";

    /// <summary>A provider whose ComfyUI client carries credentials, and whose credential-less client for any
    /// other origin is a stub of its own — so a test can see which client each fetch went through.</summary>
    private static (ComfyUiProvider Provider, StubHttpHandler Comfy, StubHttpHandler Foreign) WithForeign(
        ComfyUiOptions? options = null)
    {
        var comfy = new StubHttpHandler();
        var foreign = new StubHttpHandler();
        var provider = new ComfyUiProvider(
            options ?? new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () =>
            {
                var client = new HttpClient(comfy, disposeHandler: false);
                client.DefaultRequestHeaders.Authorization = new("Bearer", Secret);
                client.DefaultRequestHeaders.Add("api-key", Secret);
                return client;
            },
            foreign);
        return (provider, comfy, foreign);
    }

    private static MediaRequest FromUri(string uri) =>
        MeshAsk(Bound("input-path", "1.inputs.model_file"), new MediaInput("model/gltf-binary", Uri: uri));

    private const string OwnView = "http://127.0.0.1:8188/view?filename=a.glb&subfolder=3d&type=output";

    [Fact]
    public async Task A_URI_on_another_origin_is_fetched_WITHOUT_the_ComfyUI_clients_credentials()
    {
        var (provider, comfy, foreign) = WithForeign();
        foreign.Enqueue(HttpStatusCode.OK, "FOREIGN-MESH", "application/octet-stream");
        comfy.Enqueue(HttpStatusCode.OK, Uploaded("f.glb", "3d")).Enqueue(HttpStatusCode.OK, """{"prompt_id":"1"}""");

        var operation = await provider.SubmitAsync(FromUri("https://cdn.example.org/models/f.glb"));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        var fetch = Assert.Single(foreign.Requests);
        Assert.Equal("https://cdn.example.org/models/f.glb", fetch.Uri?.ToString());
        Assert.Null(fetch.Auth);
        Assert.Null(fetch.ApiKeyHeader);
        Assert.Equal(2, comfy.Requests.Count);    // the upload and the queue call, never the fetch
        Assert.Equal("FOREIGN-MESH", FormField(comfy.Requests[0].Body, "image"));
    }

    [Fact]
    public async Task A_redirect_from_ComfyUIs_own_origin_to_another_host_is_followed_WITHOUT_the_credentials()
    {
        var (provider, comfy, foreign) = WithForeign();
        comfy.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://elsewhere.example/x.glb") },
            })
            .Enqueue(HttpStatusCode.OK, Uploaded("x.glb", "3d")).Enqueue(HttpStatusCode.OK, """{"prompt_id":"1"}""");
        foreign.Enqueue(HttpStatusCode.OK, "REDIRECTED-MESH", "application/octet-stream");

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Equal(OwnView, comfy.Requests[0].Uri?.ToString());   // its own origin: its own credentials
        var hop = Assert.Single(foreign.Requests);
        Assert.Equal("https://elsewhere.example/x.glb", hop.Uri?.ToString());
        Assert.Null(hop.Auth);
        Assert.Null(hop.ApiKeyHeader);
        Assert.Equal("REDIRECTED-MESH", FormField(comfy.Requests[1].Body, "image"));
    }

    [Fact]
    public async Task A_client_that_followed_a_redirect_off_ComfyUIs_origin_by_itself_has_its_answer_refused()
    {
        // a client the host supplies may follow redirects on its own; its answer from elsewhere is not used
        var (provider, comfy, foreign) = WithForeign();
        comfy.Enqueue(request =>
        {
            request.RequestUri = new Uri("https://elsewhere.example/x.glb");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("X"), RequestMessage = request };
        });

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("elsewhere.example", operation.Detail);
        Assert.Single(comfy.Requests);            // nothing uploaded, nothing queued
        Assert.Empty(foreign.Requests);
    }

    [Fact]
    public async Task A_fetch_whose_declared_length_is_over_MaxFetchBytes_is_refused_and_nothing_is_uploaded()
    {
        var (provider, comfy, _) = WithForeign(new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", MaxFetchBytes = 8 });
        comfy.Enqueue(HttpStatusCode.OK, "TWENTY-BYTES-OF-MESH", "application/octet-stream");

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("MaxFetchBytes", operation.Detail);
        Assert.Contains("input 1", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Single(comfy.Requests);
    }

    [Fact]
    public async Task A_body_that_outgrows_its_declared_length_is_cut_at_MaxFetchBytes_while_it_streams()
    {
        var (provider, comfy, _) = WithForeign(new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", MaxFetchBytes = 8 });
        comfy.Enqueue(_ =>
        {
            var content = new ByteArrayContent(Encoding.ASCII.GetBytes("TWENTY-BYTES-OF-MESH"));
            content.Headers.ContentLength = 4;    // a header that lies: only the count while reading can tell
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("MaxFetchBytes", operation.Detail);
        Assert.Single(comfy.Requests);
    }

    [Theory]
    [InlineData("file:///C:/models/x.glb")]
    [InlineData("/view?filename=a.glb&type=output")]
    [InlineData("ftp://files.example/x.glb")]
    [InlineData("not a uri at all")]
    public async Task Only_an_absolute_http_or_https_URI_is_fetched_and_anything_else_is_refused_before_any_call(
        string uri)
    {
        var (provider, comfy, foreign) = WithForeign();

        var operation = await provider.SubmitAsync(FromUri(uri));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("input 1", operation.Detail);
        Assert.Contains($"fetching {uri}", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(comfy.Requests);
        Assert.Empty(foreign.Requests);
    }

    [Fact]
    public async Task A_fetch_that_throws_keeps_the_inputs_name_and_the_URI_in_the_detail()
    {
        var (provider, comfy, _) = WithForeign();
        comfy.Enqueue(_ => throw new InvalidOperationException("the handler blew up"));

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("input 1", operation.Detail);
        Assert.Contains($"fetching {OwnView}", operation.Detail);
        Assert.Contains("the handler blew up", operation.Detail);
    }

    [Fact]
    public async Task A_foreign_fetch_that_answers_an_error_is_the_inputs_fault_even_a_5xx()
    {
        // another server's 503 says nothing about ComfyUI's health — the same code from ComfyUI itself would
        var (provider, _, foreign) = WithForeign();
        foreign.Enqueue(HttpStatusCode.ServiceUnavailable, "down");

        var operation = await provider.SubmitAsync(FromUri("https://cdn.example.org/f.glb"));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
    }

    [Fact]
    public async Task A_foreign_fetch_that_throws_is_the_inputs_fault()
    {
        var (provider, _, foreign) = WithForeign();
        foreign.Enqueue(_ => throw new HttpRequestException("connection refused"));

        var operation = await provider.SubmitAsync(FromUri("https://cdn.example.org/f.glb"));

        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Contains("connection refused", operation.Detail);
    }

    [Fact]
    public async Task A_redirect_to_a_scheme_other_than_http_is_refused()
    {
        var (provider, comfy, foreign) = WithForeign();
        comfy.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("ftp://files.example/x.glb") },
        });

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("not http or https", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Single(comfy.Requests);
        Assert.Empty(foreign.Requests);
    }

    [Fact]
    public async Task More_than_five_redirects_are_refused()
    {
        var (provider, comfy, _) = WithForeign();
        comfy.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://127.0.0.1:8188/view?filename=again.glb&type=output") },
        });   // the stub repeats it for every hop

        var operation = await provider.SubmitAsync(FromUri(OwnView));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("more than 5", operation.Detail);
        Assert.Equal(6, comfy.Requests.Count);   // the first request and five redirects, no upload
    }

    private static ComfyUiProvider WithStalledForeign(ComfyUiOptions options) =>
        new(options, () => new HttpClient(new StubHttpHandler(), disposeHandler: false), new StallingHandler());

    [Fact]
    public async Task A_stalled_foreign_fetch_is_bounded_by_FetchTimeout_even_when_the_submit_has_no_deadline()
    {
        var provider = WithStalledForeign(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            Timeout = Timeout.InfiniteTimeSpan,
            FetchTimeout = TimeSpan.FromMilliseconds(150),
        });

        var operation = await provider.SubmitAsync(FromUri("https://cdn.example.org/f.glb")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("FetchTimeout", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.False(operation.Inconclusive);
    }

    [Fact]
    public async Task The_submit_deadline_firing_during_a_foreign_fetch_is_the_inputs_fault_and_not_inconclusive()
    {
        var provider = WithStalledForeign(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            Timeout = TimeSpan.FromMilliseconds(150),
        });

        var operation = await provider.SubmitAsync(FromUri("https://cdn.example.org/f.glb")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("nothing was queued", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.False(operation.Inconclusive);
    }

    [Fact]
    public async Task Empty_bytes_and_no_URI_is_an_input_with_neither_and_is_refused()
    {
        // zero bytes are no image, as the sibling backends read them — uploading them would load nothing
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"),
            new MediaInput("model/gltf-binary", Data: [])));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("neither bytes nor a URI", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_input_bound_to_the_field_the_prompt_was_written_to_is_refused()
    {
        // the upload's name would silently replace the caller's prompt
        var (provider, http) = Provider();
        var paths = Bound("input-path", "1.inputs.model_file");
        paths["prompt-path"] = "1.inputs.model_file";

        var operation = await provider.SubmitAsync(MeshAsk(paths, Mesh("MESH")) with { Prompt = "a cube" });

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("prompt", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    // ---- a timed-out submit is inconclusive only once the queue call has been sent -----------------------

    private sealed class StallingHandler : HttpMessageHandler
    {
        public List<Uri?> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request.RequestUri);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static (ComfyUiProvider Provider, StallingHandler Http) Stalled()
    {
        var stalling = new StallingHandler();
        var provider = new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188", Timeout = TimeSpan.FromMilliseconds(150) },
            () => new HttpClient(stalling, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) });
        return (provider, stalling);
    }

    [Fact]
    public async Task A_timeout_while_UPLOADING_is_not_inconclusive_because_nothing_was_queued()
    {
        var (provider, stalling) = Stalled();

        var operation = await provider.SubmitAsync(MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("M")));

        Assert.Equal("http://127.0.0.1:8188/upload/image", Assert.Single(stalling.Seen)?.ToString());
        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.False(operation.Inconclusive);
        Assert.Contains("nothing was queued", operation.Detail);
    }

    [Fact]
    public async Task A_timeout_on_the_QUEUE_call_is_inconclusive_because_the_workflow_may_have_been_accepted()
    {
        var (provider, stalling) = Stalled();

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal("http://127.0.0.1:8188/prompt", Assert.Single(stalling.Seen)?.ToString());
        Assert.True(operation.Inconclusive);
        Assert.Contains("may still have been accepted", operation.Detail);
    }

    [Fact]
    public async Task Every_upload_gets_its_own_name_and_never_asks_to_overwrite()
    {
        // two jobs uploading onto one shared name would each load whichever file landed last
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, Uploaded("a.glb", "3d")).Enqueue(HttpStatusCode.OK, """{"prompt_id":"1"}""")
            .Enqueue(HttpStatusCode.OK, Uploaded("b.glb", "3d")).Enqueue(HttpStatusCode.OK, """{"prompt_id":"2"}""");
        var ask = MeshAsk(Bound("input-path", "1.inputs.model_file"), Mesh("MESH"));

        await provider.SubmitAsync(ask);
        await provider.SubmitAsync(ask);

        var first = UploadedFileName(http.Requests[0].Body);
        var second = UploadedFileName(http.Requests[2].Body);
        Assert.False(string.IsNullOrEmpty(first));
        Assert.NotEqual(first, second);
        Assert.Null(FormField(http.Requests[0].Body, "overwrite"));
    }

    [Fact]
    public async Task The_upload_path_the_mesh_subfolder_and_the_path_option_key_are_host_options()
    {
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            UploadPath = "api/upload/image",
            MeshSubfolder = "meshes",
            InputPathOption = "load-into",
        });
        http.Enqueue(HttpStatusCode.OK, Uploaded("m.glb", "meshes")).Enqueue(HttpStatusCode.OK, """{"prompt_id":"1"}""");

        var operation = await provider.SubmitAsync(
            MeshAsk(Bound("load-into", "1.inputs.model_file"), Mesh("MESH")));

        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/api/upload/image", http.Requests[0].Uri?.ToString());
        Assert.Equal("meshes", FormField(http.Requests[0].Body, "subfolder"));
        Assert.Equal("meshes/m.glb", Posted(http.Requests[1].Body, "1", "model_file"));
    }

    [Fact]
    public async Task A_mesh_output_filed_under_the_3d_collection_comes_back_as_a_gltf_binary_artifact()
    {
        // MEASURED: SaveGLB files under "3d", and view serves it as octet-stream — the extension is the signal
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"3":{"3d":[{"filename":"lyntai_00001_.glb","subfolder":"3d","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk, result.Detail);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("model/gltf-binary", artifact.MediaType);
        Assert.Contains("subfolder=3d", artifact.Uri);
    }

    [Theory]
    [InlineData("scene.gltf", "model/gltf+json")]
    [InlineData("mesh.obj", "model/obj")]
    [InlineData("print.STL", "model/stl")]
    public async Task The_other_mesh_extensions_map_to_their_model_media_types(string filename, string expected)
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"3":{"3d":[{"filename":"FILENAME","subfolder":"3d","type":"output"}]}}}}
            """.Replace("FILENAME", filename));

        var result = await provider.FetchAsync("abc-123");

        Assert.Equal(expected, Assert.Single(result.Artifacts).MediaType);
    }

    [Fact]
    public async Task Inline_generation_is_declined_because_this_backend_is_a_job_backend()
    {
        // the base seam must answer honestly rather than hiding a poll loop
        var (provider, http) = Provider();

        var result = await provider.GenerateAsync(Ask());

        Assert.Equal(ProviderVerdict.Unsupported, result.Verdict);
        Assert.Contains("submit", result.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_submit_posts_the_callers_workflow_and_returns_the_prompt_id()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"abc-123\",\"number\":1}");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal("abc-123", operation.Id);
        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/prompt", http.Requests[0].Uri?.ToString());
        Assert.Contains("KSampler", http.Requests[0].Body);       // the caller's graph went through
    }

    [Fact]
    public async Task The_prompt_is_substituted_into_the_node_the_caller_named()
    {
        // a graph backend has no "prompt" field of its own — the caller says WHERE the text belongs
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"abc-123\"}");

        await provider.SubmitAsync(Ask("a blue circle"));

        Assert.Contains("a blue circle", http.Requests[0].Body);
        Assert.DoesNotContain("placeholder", http.Requests[0].Body);
    }

    [Fact]
    public async Task A_submit_without_a_workflow_is_refused_rather_than_invented()
    {
        // there is no sensible default graph: guessing one would silently produce something the caller
        // never described
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask(workflow: null));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("workflow", operation.Detail);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);   // another backend may need no graph
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task An_invalid_workflow_json_is_refused_before_it_is_sent()
    {
        var (provider, http) = Provider();

        var operation = await provider.SubmitAsync(Ask(workflow: "{not json"));

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.Unsupported, operation.Verdict);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Polling_an_unfinished_run_reports_running_not_a_failure()
    {
        // ComfyUI's history is EMPTY until the run lands; "not there yet" must not read as "it failed"
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/history/abc-123", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task A_transport_failure_while_polling_keeps_the_run_alive_rather_than_abandoning_it()
    {
        // the server not answering says nothing about the render. Failed here reaches the job handler as
        // JobOutcome.Fail with no retry, so a restarted ComfyUI would abandon a run that is still going —
        // the same rule fal's poll already follows.
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.InternalServerError, "{\"error\":\"upstream hiccup\"}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Running, operation.Status);
        Assert.Contains("hiccup", operation.Detail);
    }

    [Fact]
    public async Task A_4xx_while_polling_is_terminal_so_a_wrong_id_or_path_cannot_poll_forever()
    {
        // the other half of the rule: a 404 means THIS id (or the guessed HistoryPath) will never resolve,
        // and reporting Running would leave the job polling a render nobody holds
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.NotFound, "{\"error\":\"unknown prompt id\"}");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
    }

    [Fact]
    public async Task Submitting_to_an_unconfigured_backend_is_NotConfigured_so_routing_advances_without_blame()
    {
        var (provider, http) = Provider(new ComfyUiOptions { BaseUrl = "" });   // never configured

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Equal(ProviderVerdict.NotConfigured, operation.Verdict);   // never set up is not a fault (D31)
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Polling_an_unconfigured_backend_is_terminal_rather_than_perpetually_running()
    {
        var (provider, http) = Provider(new ComfyUiOptions { BaseUrl = "" });   // never configured

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("BaseUrl", operation.Detail);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Polling_a_finished_run_reports_succeeded()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}
            """);

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.Progress);
    }

    // ---- a run that fails DURING execution: measured on 0.36.0 (a GLB with a node and no mesh) ------------

    /// <summary>The history entry of a graph that raised at run time, in the shape measured. The traceback and
    /// inputs carry a marker standing in for the server's file paths.</summary>
    private const string ExecutionError = """
        {"abc-123":{"outputs":{},"status":{"status_str":"error","completed":false,"messages":[
          ["execution_start",{"prompt_id":"abc-123","timestamp":1}],
          ["execution_cached",{"nodes":[],"prompt_id":"abc-123","timestamp":2}],
          ["execution_error",{"prompt_id":"abc-123","node_id":"2","node_type":"Get3DComponents","executed":["1"],
            "exception_message":"Get3DComponents: no triangle geometry found in the glTF scene",
            "exception_type":"ValueError",
            "traceback":["  File \"/srv/SERVER-PATH-MARKER/execution.py\", line 510, in execute\n"],
            "current_inputs":{"model_3d":["/srv/SERVER-PATH-MARKER/input/3d/x.glb"]},
            "current_outputs":{},"timestamp":3}]]}}}
        """;

    [Fact]
    public async Task A_run_that_failed_during_execution_polls_as_FAILED_with_the_nodes_message_and_no_server_paths()
    {
        // it read as "still running" until the caller's own deadline, so a broken graph looked like a slow one
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, ExecutionError);

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("no triangle geometry found in the glTF scene", operation.Detail);
        Assert.Contains("Get3DComponents", operation.Detail);
        Assert.Contains("node 2", operation.Detail);
        Assert.DoesNotContain("SERVER-PATH-MARKER", operation.Detail);   // never the traceback or the inputs
        Assert.DoesNotContain("execution.py", operation.Detail);
    }

    [Fact]
    public async Task An_error_entry_with_no_execution_error_message_still_polls_as_failed()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """{"abc-123":{"outputs":{},"status":{"status_str":"error","completed":false,"messages":[]}}}""");

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.False(string.IsNullOrWhiteSpace(operation.Detail));
    }

    [Fact]
    public async Task A_run_that_succeeded_with_a_status_text_still_polls_as_succeeded()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"status_str":"success","completed":true,"messages":[["execution_success",{"prompt_id":"abc-123"}]]},
             "outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}
            """);

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Succeeded, operation.Status);
    }

    [Fact]
    public async Task Fetching_a_run_that_failed_reports_its_error_rather_than_not_finished()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, ExecutionError);

        var result = await provider.FetchAsync("abc-123");

        Assert.False(result.IsOk);
        Assert.Contains("no triangle geometry", result.Detail);
        Assert.DoesNotContain("SERVER-PATH-MARKER", result.Detail);
    }

    [Fact]
    public async Task The_failure_status_its_messages_and_the_error_event_are_host_options()
    {
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            StatusTextField = "state",
            FailedStatusText = "failed",
            MessagesField = "log",
            ExecutionErrorEvent = "node_failed",
        });
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"outputs":{},"status":{"state":"failed","completed":false,"log":[
              ["node_failed",{"node_id":"7","node_type":"SaveGLB","exception_message":"disk full"}]]}}}
            """);

        var operation = await provider.PollAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("disk full", operation.Detail);
    }

    [Fact]
    public async Task A_fetch_turns_the_history_outputs_into_VIEW_uri_artifacts()
    {
        // the bytes stay on the local server until the caller wants them — same rule as a signed URL from a
        // hosted backend, and it keeps a 100 MB video out of memory by default
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"sub","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
        var uri = result.Artifacts[0].Uri;
        Assert.Contains("/view?", uri);
        Assert.Contains("filename=out.png", uri);
        Assert.Contains("subfolder=sub", uri);
        Assert.Contains("type=output", uri);
    }

    [Fact]
    public async Task A_fetch_of_a_video_output_is_recognised_by_its_extension()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"status":{"completed":true},"outputs":{"9":{"gifs":[{"filename":"clip.mp4","subfolder":"","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
        Assert.Equal("video/mp4", result.Artifacts[0].MediaType);
    }

    [Fact]
    public async Task A_fetch_before_the_run_finishes_is_a_failure_with_a_reason_not_an_empty_success()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var result = await provider.FetchAsync("abc-123");

        Assert.False(result.IsOk);
        Assert.Contains("not finished", result.Detail);
    }

    [Fact]
    public async Task Cancelling_interrupts_the_running_job()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{}");

        var operation = await provider.CancelAsync("abc-123");

        Assert.Equal(QueuedOperationStatus.Cancelled, operation.Status);
        Assert.Equal("http://127.0.0.1:8188/interrupt", http.Requests[0].Uri?.ToString());
    }

    [Fact]
    public async Task An_unreachable_local_server_is_NOT_CONFIGURED_on_every_path()
    {
        var handler = new StubHttpHandler().Enqueue(_ => throw new HttpRequestException("connection refused"));
        var provider = new ComfyUiProvider(
            new ComfyUiOptions { BaseUrl = "http://127.0.0.1:8188" },
            () => new HttpClient(handler, disposeHandler: false));

        var submitted = await provider.SubmitAsync(Ask());
        var probe = await provider.ProbeAsync();

        Assert.Equal(QueuedOperationStatus.Failed, submitted.Status);
        Assert.Contains("not reachable", submitted.Detail);
        Assert.False(probe.Available);
    }

    [Fact]
    public async Task A_probe_reads_system_stats_and_never_generates()
    {
        var (provider, http) = Provider();
        http.Enqueue(HttpStatusCode.OK, "{\"system\":{\"comfyui_version\":\"0.3.40\",\"python_version\":\"3.12\"}}");

        var probe = await provider.ProbeAsync();

        Assert.True(probe.Available);
        Assert.Equal("http://127.0.0.1:8188/system_stats", http.Requests[0].Uri?.ToString());
        Assert.Equal("0.3.40", probe.Version);
    }

    [Fact]
    public async Task Every_endpoint_path_is_overridable_because_the_surface_is_unverified()
    {
        // the guard against having guessed wrong: a host can retarget any path without waiting for a release
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            SubmitPath = "api/prompt",
            HistoryPath = "api/history",
            ViewPath = "api/view",
            InterruptPath = "api/interrupt",
            SystemStatsPath = "api/system_stats",
        });
        http.Enqueue(HttpStatusCode.OK, "{\"prompt_id\":\"x\"}");

        await provider.SubmitAsync(Ask());

        Assert.Equal("http://127.0.0.1:8188/api/prompt", http.Requests[0].Uri?.ToString());
    }

    // ---- the unmeasured surface is CORRECTABLE without a library release ------------------------------
    //
    // This class's own header says every endpoint path is settable because the surface was never measured
    // against a running server. The response FIELD names were not, and they fail more quietly than a path
    // does: a wrong path is a 404 on the first call, a wrong field name means a submitted render is never
    // recognised as accepted or a finished one is polled forever. These pin that both are now correctable.

    [Fact]
    public async Task A_host_can_retarget_the_field_carrying_the_accepted_runs_id()
    {
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            PromptIdField = "job_id",
        });
        http.Enqueue(HttpStatusCode.OK, """{"job_id":"abc-123","number":1}""");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal("abc-123", operation.Id);
        Assert.Equal(QueuedOperationStatus.Queued, operation.Status);
    }

    [Fact]
    public async Task A_missing_id_names_the_CONFIGURED_field_so_the_message_matches_what_was_looked_for()
    {
        // A failure that names "prompt_id" while the host configured "job_id" sends them hunting for the
        // wrong thing — the message has to describe the search that actually happened.
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            PromptIdField = "job_id",
        });
        http.Enqueue(HttpStatusCode.OK, """{"prompt_id":"abc-123"}""");

        var operation = await provider.SubmitAsync(Ask());

        Assert.Equal(QueuedOperationStatus.Failed, operation.Status);
        Assert.Contains("job_id", operation.Detail);
    }

    [Fact]
    public async Task A_host_can_retarget_the_completion_signal_and_the_outputs_container()
    {
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            StatusField = "state",
            CompletedField = "done",
            OutputsField = "results",
        });
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"state":{"done":true},"results":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Contains("filename=out.png", artifact.Uri);
    }

    [Fact]
    public async Task Retargeting_the_outputs_container_keeps_it_working_as_the_fallback_completion_signal()
    {
        // Its PRESENCE is what says "finished" when the status block is absent or shaped unexpectedly. That
        // defensive path has to follow the rename too, or a host who retargets one field loses the other.
        var (provider, http) = Provider(new ComfyUiOptions
        {
            BaseUrl = "http://127.0.0.1:8188",
            OutputsField = "results",
        });
        http.Enqueue(HttpStatusCode.OK, """
            {"abc-123":{"results":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}
            """);

        var result = await provider.FetchAsync("abc-123");

        Assert.True(result.IsOk);
    }
}
