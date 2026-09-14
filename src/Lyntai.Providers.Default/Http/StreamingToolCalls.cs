using System.Text;
using System.Text.Json;

namespace Lyntai.Llm;

/// <summary>One line's worth of a streamed tool call. Vendors send these in PIECES — an id and name on the
/// first line for a given <paramref name="Index"/>, then argument text a few characters at a time — so a
/// single delta is almost never a usable call.</summary>
/// <param name="Index">The vendor's slot number. Fragments are joined by this, NOT by arrival order: a model
/// calling two tools interleaves their argument fragments.</param>
/// <param name="Id">The call id, on whichever line carries it. Absent for dialects that issue none.</param>
/// <param name="Name">The function name, on whichever line carries it.</param>
/// <param name="Arguments">A piece of the arguments JSON — already normalized to text, so an object-valued
/// arguments member (which arrives complete) and a string fragment accumulate through the same path.</param>
internal sealed record ToolCallDelta(int Index, string? Id, string? Name, string? Arguments);

/// <summary>Assembles <see cref="ToolCallDelta"/>s into COMPLETE <see cref="LlmToolCall"/>s.
///
/// <para><b>Why the provider does this rather than the consumer.</b> <see cref="LlmChunk.ToolCall"/> promises
/// a complete call, so partial JSON never reaches a consumer who would then have to buffer it, know the
/// vendor's fragmentation rules, and get the join right. Doing it once here is the same reasoning that puts
/// the terminal-chunk guarantee in the generation router rather than in every backend.</para>
///
/// <para><b>It produces the same shapes as the non-streaming path</b> (<c>ExtractToolCalls</c>) on purpose:
/// an id synthesized as <c>call_{index}</c> when the dialect gives none, arguments defaulting to <c>{}</c>,
/// and a fragment with no function name skipped rather than guessed at. Two implementations of one contract
/// that disagree is the defect class the 3.0 review found most of, so the rules are stated once here and
/// asserted against the other path by test.</para></summary>
internal sealed class StreamingToolCalls
{
    private readonly Dictionary<int, Slot> _slots = [];

    private sealed class Slot
    {
        public string? Id;
        public string? Name;
        public readonly StringBuilder Arguments = new();
    }

    /// <summary>Whether anything has been accumulated — the signal a stream saw tool calls at all.</summary>
    public bool Any => _slots.Count > 0;

    /// <summary>Fold one line's deltas in. Later lines fill in what earlier ones left null; argument text
    /// APPENDS. An id or name arriving twice keeps the first, because a vendor repeating them is restating
    /// rather than correcting.</summary>
    public void Add(IEnumerable<ToolCallDelta> deltas)
    {
        foreach (var delta in deltas)
        {
            if (!_slots.TryGetValue(delta.Index, out var slot)) _slots[delta.Index] = slot = new Slot();
            slot.Id ??= delta.Id;
            slot.Name ??= delta.Name;
            if (delta.Arguments is { Length: > 0 }) slot.Arguments.Append(delta.Arguments);
        }
    }

    /// <summary>The assembled calls, in slot order. A slot with no NAME is dropped: it is not a call anything
    /// can act on, and inventing one would fabricate an action.</summary>
    public IReadOnlyList<LlmToolCall> Build()
    {
        var calls = new List<LlmToolCall>();
        foreach (var (index, slot) in _slots.OrderBy(e => e.Key))
        {
            if (slot.Name is not { Length: > 0 }) continue;
            var arguments = slot.Arguments.ToString();
            calls.Add(new LlmToolCall(
                slot.Id ?? $"call_{index}",          // matches ExtractToolCalls' synthesized id exactly
                slot.Name,
                arguments.Length > 0 ? arguments : "{}"));
        }
        return calls;
    }

    /// <summary>Read a line's <c>tool_calls</c> array into deltas. Handles both dialects: an <c>index</c> when
    /// the vendor numbers its slots and positional order when it does not, string arguments (accumulated) and
    /// object arguments (complete, taken as raw text).</summary>
    /// <remarks>Everything is copied OUT of the <see cref="JsonElement"/> here. The caller parses inside a
    /// <c>using var doc</c>, so an element that outlived the document would read freed memory — a trap that
    /// does not fail loudly.</remarks>
    public static IReadOnlyList<ToolCallDelta>? Read(JsonElement container)
    {
        if (!container.TryGetProperty("tool_calls", out var calls) ||
            calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
            return null;

        var deltas = new List<ToolCallDelta>();
        var position = 0;
        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object) { position++; continue; }

            var index = call.TryGetProperty("index", out var indexElement) &&
                        indexElement.ValueKind == JsonValueKind.Number &&
                        indexElement.TryGetInt32(out var parsed)
                ? parsed
                : position;

            string? id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;

            string? name = null, arguments = null;
            if (call.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
            {
                if (fn.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                    name = nameElement.GetString();
                if (fn.TryGetProperty("arguments", out var argumentsElement))
                    arguments = argumentsElement.ValueKind switch
                    {
                        JsonValueKind.String => argumentsElement.GetString(),   // a fragment to append
                        JsonValueKind.Object => argumentsElement.GetRawText(),  // complete in one go
                        _ => null,
                    };
            }

            // An id-only or arguments-only delta is legitimate — that is what fragmentation looks like — so
            // it is recorded rather than skipped. Build() is where a slot that never got a name is dropped.
            deltas.Add(new ToolCallDelta(index, id, name, arguments));
            position++;
        }
        return deltas.Count > 0 ? deltas : null;
    }
}
