using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Text;

namespace Lyntai.Tests.Text;

/// <summary>The shared reflection-free tool-argument serializer — the boxed-primitive/JsonElement/JsonNode
/// → JSON switch that ToolFunction (MCP) and ExtensionsAiProvider (MEAI) both need. Pins that primitives
/// keep their JSON type (a 3 stays a number, not "3").</summary>
public class JsonArgsTests
{
    [Fact]
    public void Serialize_preserves_primitive_json_types()
    {
        var args = new Dictionary<string, object?>
        {
            ["i"] = 3,
            ["l"] = 9_000_000_000L,
            ["d"] = 1.5,
            ["b"] = true,
            ["s"] = "hi",
            ["nul"] = null,
        };

        var json = JsonArgs.Serialize(args);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("i").ValueKind);
        Assert.Equal(3, root.GetProperty("i").GetInt32());
        Assert.Equal(9_000_000_000L, root.GetProperty("l").GetInt64());
        Assert.Equal(1.5, root.GetProperty("d").GetDouble());
        Assert.Equal(JsonValueKind.True, root.GetProperty("b").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("s").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nul").ValueKind);
    }

    [Fact]
    public void Serialize_passes_through_json_element_and_node_by_value()
    {
        var element = JsonDocument.Parse("""{"nested":[1,2]}""").RootElement.GetProperty("nested");
        var node = JsonNode.Parse("""{"k":"v"}""");
        var args = new Dictionary<string, object?> { ["arr"] = element, ["obj"] = node };

        var json = JsonArgs.Serialize(args);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("arr").ValueKind);
        Assert.Equal("v", doc.RootElement.GetProperty("obj").GetProperty("k").GetString());
    }

    [Fact]
    public void Serialize_null_or_empty_is_empty_object()
    {
        Assert.Equal("{}", JsonArgs.Serialize(null));
        Assert.Equal("{}", JsonArgs.Serialize(new Dictionary<string, object?>()));
    }

    [Fact]
    public void ToNode_maps_null_to_null()
    {
        Assert.Null(JsonArgs.ToNode(null));
    }
}
