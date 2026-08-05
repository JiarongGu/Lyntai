using System.Reflection;
using Lyntai.Storage;
using PostgresRunner = Lyntai.Storage.Postgres.Migrations.MigrationRunnerService;
using SqliteRunner = Lyntai.Storage.Sqlite.Migrations.MigrationRunnerService;

namespace Lyntai.Tests.Storage;

/// <summary>The tag convention every migration must follow, gated rather than remembered. It is load-bearing
/// in BOTH directions and silent in both: a migration missing <see cref="StorageFeatures.AllTag"/> never runs
/// on the default <see cref="StorageFeature.All"/> path, so its tables simply never land; a migration
/// carrying NO tag at all runs on EVERY pass — including a feature set that excludes it — which breaks the
/// "a disabled feature lands no table" promise <see cref="SelectiveMigrationTests"/> and
/// <c>PostgresStorageTests</c> rest on. Neither failure reports anything, and the <c>new-migration</c>
/// scaffold emits only <c>[Migration(...)]</c>, so the omission is one forgotten line away.
///
/// <para>Attributes are matched by NAME rather than by type, for the same reason <c>ApiSurface.Render</c>
/// does it: the check needs no FluentMigrator reference of its own.</para></summary>
public class MigrationTagConventionTests
{
    private const string MigrationAttribute = "FluentMigrator.MigrationAttribute";
    private const string TagsAttribute = "FluentMigrator.TagsAttribute";

    [Fact]
    public void Every_migration_carries_its_feature_tag_and_the_all_tag()
    {
        Assembly[] assemblies = [typeof(SqliteRunner).Assembly, typeof(PostgresRunner).Assembly];

        var features = Enum.GetValues<StorageFeature>()
            .Where(f => f is not (StorageFeature.None or StorageFeature.All))
            .Select(f => f.ToString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            var migrations = assembly.GetTypes().Where(t => Carries(t, MigrationAttribute)).ToList();

            // the query itself must not be the thing that passes: a renamed attribute would otherwise turn
            // the whole test vacuously green
            Assert.NotEmpty(migrations);

            foreach (var migration in migrations)
            {
                var tags = TagNames(migration).ToList();
                var name = $"{assembly.GetName().Name}.{migration.Name}";
                var carried = $"carries [{string.Join(", ", tags)}]";

                Assert.True(tags.Contains(StorageFeatures.AllTag),
                    $"{name} needs StorageFeatures.AllTag — without it the migration never runs on the " +
                    $"default StorageFeature.All path and its tables never land; it {carried}.");
                Assert.True(tags.Count(features.Contains) == 1,
                    $"{name} needs EXACTLY one StorageFeature tag — with none it runs even under a feature " +
                    $"set that excludes it; it {carried}.");
            }
        }
    }

    private static bool Carries(Type type, string attributeFullName) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == attributeFullName);

    /// <summary>Every tag name on the type, flattened across the <c>[Tags]</c> overloads: the string
    /// overloads carry one tag per argument, the params overload carries an array, and the
    /// <c>TagBehavior</c> overload leads with an enum value that is not a tag at all.</summary>
    private static IEnumerable<string> TagNames(Type migration)
    {
        foreach (var attribute in migration.GetCustomAttributesData()
                     .Where(a => a.AttributeType.FullName == TagsAttribute))
        {
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is string tag)
                {
                    yield return tag;
                }
                else if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> array)
                {
                    foreach (var element in array)
                        if (element.Value is string nested) yield return nested;
                }
            }
        }
    }
}
