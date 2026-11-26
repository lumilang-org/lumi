namespace Lumi.Core.Semantics;

/// <summary>
///     Helpers for pointer type names. A pointer type is written <c>*T</c>; array types are
///     recognised first, so <c>*i32[3]</c> is an array of three pointers and a pointer to an
///     array is spelled <c>*(i32[3])</c>.
/// </summary>
public static class PointerTypes
{
    public static bool IsPointer(string type)
    {
        return type.StartsWith('*') && !type.EndsWith(']');
    }

    public static string Pointee(string pointerType)
    {
        var pointee = pointerType[1..];

        // `*(i32[3])` wraps the pointee in parentheses; `*(i32, i32)` is a pointer to a tuple.
        return pointee.Length > 2 && pointee[0] == '(' && pointee[^1] == ')'
               && !TupleTypes.HasTopLevelComma(pointee[1..^1])
            ? pointee[1..^1]
            : pointee;
    }

    public static string To(string pointee)
    {
        return pointee.EndsWith(']') ? $"*({pointee})" : $"*{pointee}";
    }
}
