namespace Lyntai.Tests.Api;

/// <summary>The gate on the gate. <see cref="ApiSurfaceTests"/> proves the rendered surface still EQUALS
/// its baseline; nothing there proves the rendering can SEE a given break. Anything <see cref="ApiSurface"/>
/// drops has no place in the baseline at all, so a break in it does not weaken the gate — it deletes the
/// gate for that shape, silently.
///
/// <para>Three details were dropped until 2026-08-05, each hiding a real break:</para>
/// <list type="bullet">
/// <item><b>type parameters</b> — <c>AddSemanticMemory()</c> and <c>AddSemanticMemory&lt;TEmbedder&gt;()</c>
/// rendered as the identical line, so the baseline literally held it twice; deleting either overload left a
/// baseline the gate still accepted, because the diff read as an ordinary removal of something the surviving
/// duplicate covered. Removing a public overload from a frozen surface is exactly what the gate is for;</item>
/// <item><b>parameter names</b> — only types were rendered, so a rename passed silently even though it is a
/// source break for every named-argument caller, which the README actively teaches;</item>
/// <item><b>default values</b> — a bare <c>=</c> marker recorded only that a default EXISTED, so flipping
/// one passed silently.</item>
/// </list>
///
/// <para>The fixtures below are local on purpose: pinning a real signature here would duplicate what the
/// baseline already owns and would have to be edited alongside it, which is how a gate's gate rots.</para>
/// </summary>
public class ApiSurfaceRendererTests
{
    [Fact]
    public void A_generic_overload_does_not_collapse_onto_its_non_generic_sibling()
    {
        // the reported shape, kept concrete: LyntaiBuilder really does declare both, and before the fix
        // the Lyntai.Core baseline carried "AddSemanticMemory() : LyntaiBuilder" on two consecutive lines
        var lines = Lines(ApiSurface.Render(typeof(Lyntai.LyntaiBuilder)));

        Assert.Contains("AddSemanticMemory() : LyntaiBuilder", lines);
        Assert.Contains("AddSemanticMemory<TEmbedder>() : LyntaiBuilder", lines);
    }

    /// <summary>The deletion the gate could not see, performed: two overloads render, one is dropped, and
    /// the rendering must be poorer by exactly the dropped one. Before the fix the two overloads rendered
    /// identically, so the "after" set was covered by the surviving duplicate and this difference was
    /// empty — the removal of public surface simply did not reach the baseline.</summary>
    [Fact]
    public void Dropping_a_generic_overload_changes_the_rendered_surface()
    {
        var before = Overloaded(typeof(Overloads));
        var after = Overloaded(typeof(OverloadsMinusGeneric));

        // members are rendered in ordinal order, so these sequences are exact, not incidental
        Assert.Equal(new[] { "Overloaded() : Void", "Overloaded<TItem>() : Void" }, before);
        Assert.Equal(new[] { "Overloaded() : Void" }, after);

        // the punchline: the rendering is poorer by EXACTLY the deleted overload. Before the fix both lines
        // of `before` read "Overloaded() : Void", so this difference was empty — the deletion of public
        // surface reached the baseline as nothing at all.
        Assert.Equal(new[] { "Overloaded<TItem>() : Void" }, before.Except(after, StringComparer.Ordinal));

        static List<string> Overloaded(Type t) =>
            Lines(ApiSurface.Render(t))
                .Where(l => l.StartsWith("Overloaded", StringComparison.Ordinal))
                .ToList();
    }

    [Fact]
    public void Parameter_names_are_rendered()
    {
        var lines = Lines(ApiSurface.Render(typeof(Signatures)));

        Assert.Contains("Named(String first, Int32 second) : Void", lines);
    }

    [Fact]
    public void An_optional_parameters_default_is_rendered_as_its_value()
    {
        var lines = Lines(ApiSurface.Render(typeof(Signatures)));

        // culture-invariant and C#-shaped: the whole file is diffed, so a value that formats differently
        // on another machine (or a bare `=`) turns a review into noise or hides a flipped default
        Assert.Contains(
            "Defaults(Int32 count = 3, String label = \"x\", Boolean flag = true, Double ratio = 0.5, "
            + "Nullable<TimeSpan> window = null, StringComparison how = StringComparison.Ordinal, "
            + "CancellationToken token = default) : Void",
            lines);
    }

    /// <summary>No two members of one type may render identically — the general form of the bug, gated over
    /// the same assemblies <see cref="ApiSurfaceTests"/> covers. A duplicate line is an ambiguity the
    /// baseline cannot resolve, so deleting either member still matches; catching it here means the next
    /// shape the renderer cannot tell apart arrives as a red test rather than as a hole nobody looks for.
    /// </summary>
    [Theory]
    [MemberData(nameof(ApiSurfaceTests.Assemblies), MemberType = typeof(ApiSurfaceTests))]
    public void No_two_members_of_a_type_render_identically(string assemblyName)
    {
        var duplicates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var type = "";

        foreach (var line in ApiSurface.Render(ApiSurfaceTests.Loaded[assemblyName]).Split('\n'))
        {
            if (line.Length == 0) continue;

            // a member line is indented; anything else starts a new type block
            if (!line.StartsWith("    ", StringComparison.Ordinal))
            {
                type = line;
                seen.Clear();
                continue;
            }

            if (!seen.Add(line.Trim())) duplicates.Add($"{type} :: {line.Trim()}");
        }

        Assert.True(duplicates.Count == 0,
            $"{assemblyName}: {duplicates.Count} member(s) render identically to another member of the same "
            + "type. The baseline cannot tell them apart, so DELETING one still matches it — render whatever "
            + "distinguishes them (see ApiSurface.TypeParams / Params):\n  "
            + string.Join("\n  ", duplicates));
    }

    private static List<string> Lines(string rendered) =>
        rendered.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private sealed class Signatures
    {
        public void Named(string first, int second) { }

        public void Defaults(
            int count = 3,
            string label = "x",
            bool flag = true,
            double ratio = 0.5,
            TimeSpan? window = null,
            StringComparison how = StringComparison.Ordinal,
            CancellationToken token = default) { }
    }

    private sealed class Overloads
    {
        public void Overloaded() { }

        public void Overloaded<TItem>() { }
    }

    /// <summary><see cref="Overloads"/> with the generic overload deleted — the break being simulated.
    /// </summary>
    private sealed class OverloadsMinusGeneric
    {
        public void Overloaded() { }
    }
}
