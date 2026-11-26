using System.Text;

namespace Lumi.Core.Semantics;

// A variant of a built-in generic type. `TypeParamIndex` is the type parameter its single payload
// has, or null when the variant carries nothing. `Tag` is the discriminant.
public sealed record VariantSpec(string Name, int? TypeParamIndex, int Tag);

public sealed record BuiltinGenericSpec(string Name, IReadOnlyList<string> TypeParams, IReadOnlyList<VariantSpec> Variants);

// The generic types every Lumi file can use without importing anything. Each one is declared as
// a prelude generic enum (see PreludeSource), so it is specialized like any user-written generic
// enum: `Option<i32>` becomes the concrete enum `Option__i32`.
public static class BuiltinGenericRegistry
{
    public static readonly IReadOnlyList<BuiltinGenericSpec> Specs =
    [
        new("Option", ["T"], [new("Some", 0, 0), new("None", null, 1)]),
        new("Result", ["T", "E"], [new("Ok", 0, 0), new("Err", 1, 1)])
    ];

    public static IEnumerable<string> Names => Specs.Select(spec => spec.Name);

    public static bool TryGet(string name, out BuiltinGenericSpec spec)
    {
        spec = Specs.FirstOrDefault(s => s.Name == name)!;
        return spec is not null;
    }

    // Lumi source declaring every built-in generic enum, in tag order.
    public static string PreludeSource(IEnumerable<string>? exclude = null)
    {
        var skipped = new HashSet<string>(exclude ?? []);
        var source = new StringBuilder();

        foreach (var spec in Specs.Where(s => !skipped.Contains(s.Name)))
        {
            source.AppendLine($"pub enum {spec.Name}<{string.Join(", ", spec.TypeParams)}> {{");

            foreach (var variant in spec.Variants.OrderBy(v => v.Tag))
            {
                source.AppendLine(variant.TypeParamIndex is { } index
                    ? $"    {variant.Name}({spec.TypeParams[index]})"
                    : $"    {variant.Name}");
            }

            source.AppendLine("}");
        }

        return source.ToString();
    }
}
