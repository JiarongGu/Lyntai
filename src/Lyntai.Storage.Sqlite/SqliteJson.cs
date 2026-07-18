using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Lyntai.Storage.Sqlite;

/// <summary>Reflection-based JSON for the SQLite governance stores (cached reply blobs, vector arrays). The
/// suppression is honest: this package is non-trim / non-AOT BY DESIGN (Dapper + FluentMigrator materialize
/// via reflection — see the csproj), so a consumer AOT-publishing already can't root it. Centralized here
/// so the trim analyzer stays on for the rest of the package's code.</summary>
internal static class SqliteJson
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Lyntai.Storage.Sqlite is non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Lyntai.Storage.Sqlite is non-AOT by design.")]
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Lyntai.Storage.Sqlite is non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Lyntai.Storage.Sqlite is non-AOT by design.")]
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
