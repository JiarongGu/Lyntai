using System.Reflection;
using System.Text.RegularExpressions;

namespace Lyntai.Tests.Inference.ChatClients;

/// <summary>`docs/extensions-ai.md` shows the recipes by COPY, and a copy drifts. These hold the page to the
/// files the suite runs: the code blocks are the tested source verbatim, and the usage blocks, which the
/// document gate compiles against stubs, call only members the real types have.</summary>
public partial class RecipeDocTests
{
    private const string Folder = "tests/Lyntai.Tests/Inference/ChatClients/";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lyntai.slnx"))) return dir.FullName;
        throw new InvalidOperationException("no Lyntai.slnx above the test assembly");
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative)).Replace("\r\n", "\n");

    private static string FenceAfter(string doc, string marker)
    {
        var start = doc.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has no `{marker}`");
        var from = doc.IndexOf("```csharp\n", start, StringComparison.Ordinal) + "```csharp\n".Length;
        return doc[from..doc.IndexOf("\n```\n", from, StringComparison.Ordinal)];
    }

    [Theory]
    [InlineData("ChatClientBackend.cs")]
    [InlineData("TextClientChatClient.cs")]
    public void A_recipe_block_on_the_page_is_the_tested_file_verbatim(string file)
    {
        var shown = FenceAfter(Read("docs/extensions-ai.md"), $"<!-- recipe: {Folder}{file} -->");

        // the file-scoped namespace line is the only thing the page leaves out
        var source = FileScopedNamespace().Replace(Read(Folder + file), "").TrimEnd('\n');

        Assert.Equal(source, shown);
    }

    [Theory]
    [InlineData(typeof(ChatClientBackend))]
    [InlineData(typeof(TextClientChatClient))]
    public void A_usage_block_calls_only_members_the_real_type_has(Type type)
    {
        var block = FenceAfter(Read("docs/extensions-ai.md"), $"<!-- usage-of: {type.Name} -->");

        var members = Regex.Matches(block, $@"\b{type.Name}\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        foreach (var member in members)
            Assert.Contains(type.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == member);

        // a named argument to a constructor the block calls
        foreach (var named in Regex.Matches(block, $@"new {type.Name}\([^)]*?(\w+):").Select(m => m.Groups[1].Value))
            Assert.Contains(type.GetConstructors().SelectMany(c => c.GetParameters()), p => p.Name == named);

        Assert.True(members.Count > 0 || block.Contains($"new {type.Name}("), "the block uses neither a member nor the constructor");
    }

    [GeneratedRegex(@"^namespace [^\n]+;\n\n", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();
}
