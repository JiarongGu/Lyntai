using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Lyntai.Storage.Relational;

/// <summary>Reflection-based JSON for the relational governance stores (cached replies, vector arrays). The
/// suppression is honest: the relational adapters are non-trim / non-AOT BY DESIGN (Dapper and
/// FluentMigrator materialize through reflection — see each csproj), so a consumer AOT-publishing cannot root
/// them anyway. Kept here so the trim analyzer stays on for the rest of each package.</summary>
internal static class ReflectionJson
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The relational adapters are non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The relational adapters are non-AOT by design.")]
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The relational adapters are non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The relational adapters are non-AOT by design.")]
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
