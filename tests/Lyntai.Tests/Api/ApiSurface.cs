using System.Reflection;
using System.Text;

namespace Lyntai.Tests.Api;

/// <summary>Renders a stable, sorted text description of an assembly's PUBLIC API surface — the
/// baseline the approval test compares against so any public-surface change is deliberate.</summary>
internal static class ApiSurface
{
    public static string Render(Assembly assembly)
    {
        var sb = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            // FluentMigrator migration classes are impl detail the 1.0 squash will rewrite — consumers
            // never name them, so they don't belong in the frozen surface. Detect the attribute by name
            // (mirrors IsRequired) so this test project needs no FluentMigrator reference. This drops only
            // the M<digits>_* migration classes; MigrationRunnerService / LyntaiVersionTable carry no
            // [Migration] attribute and stay in the baseline.
            if (type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "FluentMigrator.MigrationAttribute"))
                continue;

            sb.Append(Kind(type)).Append(' ').Append(type.FullName).Append('\n');
            foreach (var member in Members(type).OrderBy(m => m, StringComparer.Ordinal))
                sb.Append("    ").Append(member).Append('\n');
        }
        return sb.ToString();
    }

    // sealed/abstract are rendered because REMOVING them is non-breaking but ADDING them post-1.0 is a
    // break (a consumer may derive from an unsealed class) — the gate must see the modifier flip.
    private static string Kind(Type t) =>
        t.IsInterface ? "interface"
        : t.IsEnum ? "enum"
        : t.IsValueType ? "struct"
        : t.IsAbstract && t.IsSealed ? "static class"
        : t.IsAbstract ? "abstract class"
        : t.IsSealed ? "sealed class"
        : "class";

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var m in type.GetMembers(flags))
        {
            // public OR protected (family) surface only; skip compiler-generated + property/event accessors
            if (m is MethodInfo { IsSpecialName: true }) continue; // get_/set_/add_/remove_/op_
            if (!IsVisible(m)) continue;
            if (m.Name.StartsWith('<')) continue; // backing fields etc.

            // `static` is rendered on members (an instance→static flip is a binary/source break the gate
            // must see); `required` on properties (adding it post-1.0 breaks every object initializer).
            switch (m)
            {
                case MethodInfo method:
                    yield return $"{(method.IsStatic ? "static " : "")}{method.Name}({Params(method.GetParameters())}) : {Simple(method.ReturnType)}";
                    break;
                case PropertyInfo prop:
                    yield return $"{(prop.GetAccessors(true)[0].IsStatic ? "static " : "")}{prop.Name} : {Simple(prop.PropertyType)}{(IsRequired(prop) ? " required" : "")}";
                    break;
                case FieldInfo field:
                    yield return $"{(field.IsStatic && !field.IsLiteral ? "static " : "")}{field.Name} : {Simple(field.FieldType)}{(field.IsLiteral ? " const" : "")}";
                    break;
                case ConstructorInfo ctor:
                    yield return $".ctor({Params(ctor.GetParameters())})";
                    break;
                case EventInfo evt:
                    yield return $"event {evt.Name}";
                    break;
            }
        }
    }

    private static bool IsRequired(PropertyInfo p) =>
        p.GetCustomAttributesData().Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");

    private static bool IsVisible(MemberInfo m) => m switch
    {
        MethodBase mb => mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly,
        PropertyInfo p => IsVisible(p.GetMethod ?? (MemberInfo)p.SetMethod!),
        FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
        EventInfo e => e.AddMethod is { } a && (a.IsPublic || a.IsFamily),
        _ => false,
    };

    private static string Params(ParameterInfo[] ps) =>
        string.Join(", ", ps.Select(p => Simple(p.ParameterType) + (p.IsOptional ? "=" : "")));

    // stable, short type names — generic arity kept, namespaces dropped for readability + stability
    private static string Simple(Type t)
    {
        if (t.IsGenericParameter) return t.Name;
        if (t.IsGenericType)
        {
            var name = t.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0) name = name[..tick];
            return $"{name}<{string.Join(",", t.GetGenericArguments().Select(Simple))}>";
        }
        return t.Name;
    }
}
