using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Lyntai.Storage.Postgres;

/// <summary>Reflection-based JSON for the Postgres governance stores (cached reply blobs). Honest
/// suppression: this package is non-trim / non-AOT BY DESIGN (Npgsql + Dapper + FluentMigrator reflection —
/// see the csproj). Centralized so the trim analyzer stays on for the rest of the package.</summary>
internal static class PostgresJson
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Lyntai.Storage.Postgres is non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Lyntai.Storage.Postgres is non-AOT by design.")]
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Lyntai.Storage.Postgres is non-trim by design (reflection-based data access).")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Lyntai.Storage.Postgres is non-AOT by design.")]
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
}
