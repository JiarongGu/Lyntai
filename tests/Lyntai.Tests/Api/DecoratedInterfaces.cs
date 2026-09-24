using System.Reflection;

namespace Lyntai.Tests.Api;

/// <summary>Finds the interfaces a scanned type set decorates, and their default-bodied members.
/// <para>An interface is DECORATED when a class in the set implements it and has a constructor parameter of
/// exactly that interface, or of a collection of it (a composite). The parameter type must match exactly: a
/// record's copy constructor takes its own type, and an adapter takes a different interface.</para>
/// <para>A default body is an overridable (virtual, non-abstract) instance member, on the decorated interface
/// or on any scanned interface it extends — a decorator implements those too.</para></summary>
internal static class DecoratedInterfaces
{
    public sealed record Finding(string Member, IReadOnlyList<string> Decorators);

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Each decorated interface in <paramref name="scanned"/>, with its decorators' names, sorted.</summary>
    public static IReadOnlyDictionary<Type, IReadOnlyList<string>> Find(IReadOnlyCollection<Type> scanned)
    {
        var interfaces = scanned.Where(t => t.IsInterface).Select(Open).ToHashSet();
        var found = new Dictionary<Type, SortedSet<string>>();

        foreach (var type in scanned.Where(t => t.IsClass))
        {
            var parameters = type.GetConstructors(AnyInstance).SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType).ToArray();
            foreach (var decorated in type.GetInterfaces().Select(Open).Where(interfaces.Contains))
            {
                if (!parameters.Any(p => Open(p) == decorated || ElementOf(p) is { } e && Open(e) == decorated))
                    continue;
                if (!found.TryGetValue(decorated, out var names)) found[decorated] = names = new(StringComparer.Ordinal);
                names.Add(type.Name);
            }
        }

        return found.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)[.. kv.Value]);
    }

    /// <summary>The default bodies no allowance covers, and the allowances no default body matches. A member is
    /// keyed <c>Namespace.IType.Member(ParamType, …)</c>.</summary>
    public static (IReadOnlyList<Finding> Unallowed, IReadOnlyList<string> Stale) Check(
        IReadOnlyCollection<Type> scanned, IReadOnlyDictionary<string, string> allowances)
    {
        var interfaces = scanned.Where(t => t.IsInterface).Select(Open).ToHashSet();
        var defaults = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var (decorated, decorators) in Find(scanned))
        {
            var declaring = decorated.GetInterfaces().Select(Open).Where(interfaces.Contains).Append(decorated);
            foreach (var member in declaring.SelectMany(i => i.GetMethods(AnyInstance))
                         .Where(m => m is { IsVirtual: true, IsAbstract: false }))
            {
                var key = Key(member);
                if (!defaults.TryGetValue(key, out var names)) defaults[key] = names = new(StringComparer.Ordinal);
                names.UnionWith(decorators);
            }
        }

        return (
            [.. defaults.Where(kv => !allowances.ContainsKey(kv.Key)).Select(kv => new Finding(kv.Key, [.. kv.Value]))],
            [.. allowances.Keys.Where(k => !defaults.ContainsKey(k)).Order(StringComparer.Ordinal)]);
    }

    private static string Key(MethodInfo m) =>
        $"{Open(m.DeclaringType!).FullName}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name.TrimEnd('&')))})";

    private static Type Open(Type t) => t.IsGenericType && !t.IsGenericTypeDefinition ? t.GetGenericTypeDefinition() : t;

    private static Type? ElementOf(Type t) =>
        t.IsArray ? t.GetElementType()
        : t == typeof(string) ? null
        : (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? t
            : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
          ?.GetGenericArguments()[0];
}
