using System.Globalization;
using System.Reflection;
using System.Text;

namespace Lyntai.Tests.Api;

/// <summary>Renders a stable, sorted text description of an assembly's PUBLIC API surface — the
/// baseline the approval test compares against so any public-surface change is deliberate.
///
/// <para><b>Whatever this renderer drops, the gate cannot see.</b> A dropped detail does not weaken the
/// gate, it deletes it for that shape: the baseline simply has no place to record the change. Three
/// details were dropped until 2026-08-05, each hiding a real break — a method's TYPE PARAMETERS (so
/// <c>AddSemanticMemory()</c> and <c>AddSemanticMemory&lt;TEmbedder&gt;()</c> rendered as one identical
/// line, held twice, and deleting either overload still matched), its parameter NAMES (a rename is a
/// source break for every named-argument caller), and an optional parameter's DEFAULT VALUE (a bare
/// <c>=</c> marker made flipping a default invisible). Adding a fourth detail is cheap; noticing a
/// missing one costs an audit, so prefer rendering more. <c>ApiSurfaceRendererTests</c> is the gate on
/// this gate.</para>
///
/// <para>Every rendering decision must be DETERMINISTIC and culture-invariant — the whole output is
/// sorted and diffed, so a value that formats differently on another machine turns an unrelated review
/// into noise.</para></summary>
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

            sb.Append(Render(type));
        }
        return sb.ToString();
    }

    /// <summary>Renders ONE type's block — its header line plus its members, sorted. Split out of
    /// <see cref="Render(Assembly)"/> only so the renderer is reachable from a fixture type in
    /// <c>ApiSurfaceRendererTests</c>: the assembly path is this same code, one type at a time, so a test
    /// against a fixture is testing what the baselines are rendered by.</summary>
    public static string Render(Type type)
    {
        var sb = new StringBuilder();
        sb.Append(Kind(type)).Append(' ').Append(type.FullName).Append('\n');
        foreach (var member in Members(type).OrderBy(m => m, StringComparer.Ordinal))
            sb.Append("    ").Append(member).Append('\n');
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
                    yield return $"{(method.IsStatic ? "static " : "")}{method.Name}{TypeParams(method)}({Params(method.GetParameters())}) : {Simple(method.ReturnType)}";
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

    // Type parameters are rendered — NAMES, not just arity — because without them a generic method and a
    // non-generic sibling collapse onto the SAME line. Two identical lines carry no information about which
    // is which, so deleting either overload leaves a baseline the gate still accepts: one duplicate goes
    // away and the diff reads as an ordinary removal the survivor covers. Removing a public overload from a
    // frozen surface is precisely what this gate exists to stop.
    private static string TypeParams(MethodInfo m) =>
        m.IsGenericMethod ? $"<{string.Join(",", m.GetGenericArguments().Select(Simple))}>" : "";

    // Parameter NAMES are frozen surface, not decoration: the README teaches named arguments, so renaming
    // one is a source break for every caller using it — and a types-only rendering never saw it. The DEFAULT
    // is rendered as its VALUE rather than the old bare `=` marker for the same reason: flipping a default
    // is a behaviour change no consumer can detect at compile time, and `=` recorded only that one existed.
    private static string Params(ParameterInfo[] ps) =>
        string.Join(", ", ps.Select(p =>
            $"{Simple(p.ParameterType)} {p.Name}{(p.IsOptional ? $" = {Default(p)}" : "")}"));

    /// <summary>An optional parameter's default in C# source form, formatted culture-invariantly so the
    /// baseline is byte-identical on every machine that renders it.</summary>
    private static string Default(ParameterInfo p)
    {
        var value = p.HasDefaultValue ? p.DefaultValue : null;

        // `default(SomeStruct)`, `= null`, and `[Optional]` with no metadata constant all arrive here as
        // null (or one of reflection's two "no value" sentinels). Only a parameter that can actually hold
        // null renders as `null`, so a struct default never reads as one.
        if (value is null or DBNull or Missing)
            return p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null
                ? "default"
                : "null";

        var type = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        if (type.IsEnum) return EnumDefault(type, value);

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => Quote(s),
            char c => $"'{c}'",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default",
        };
    }

    /// <summary>An enum default as <c>Type.Member</c> so a changed default is legible rather than a bare
    /// integer. Flag combinations join with <c>|</c> (never <c>", "</c>, which would read as another
    /// parameter); a value with no name renders as the cast a caller would have to write.</summary>
    private static string EnumDefault(Type type, object value)
    {
        // Enum.Format accepts the enum type OR its underlying primitive, which is what makes this
        // indifferent to whether the runtime handed back a boxed enum or a boxed int.
        var text = Enum.Format(type, value, "G").Replace(", ", "|");
        return char.IsAsciiDigit(text[0]) || text[0] == '-'
            ? $"({Simple(type)}){text}"
            : $"{Simple(type)}.{text}";
    }

    /// <summary>A string default in C# source form. Escaping is not cosmetic: an unescaped quote or
    /// newline in a default would make the rendered line ambiguous against a different default.</summary>
    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => char.IsControl(c)
                    ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
                    : c.ToString(),
            });
        return sb.Append('"').ToString();
    }

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
