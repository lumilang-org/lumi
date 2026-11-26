namespace Lumi.Core.Semantics;

/// <summary>
///     Helpers for slice type names. A slice <c>T[]</c> is an unsized view (pointer + length) over
///     elements; it is told apart from a fixed array <c>T[N]</c> by its empty brackets.
/// </summary>
public static class SliceTypes
{
    public static bool IsSlice(string type)
    {
        return type.EndsWith("[]", StringComparison.Ordinal);
    }

    public static string Element(string sliceType)
    {
        return sliceType[..^2];
    }

    public static string Make(string elementType)
    {
        return elementType + "[]";
    }

    // The element type of a fixed array `T[N]` (also valid for slices).
    public static string ArrayElement(string type)
    {
        return type[..type.LastIndexOf('[')];
    }

    public static bool IsFixedArray(string type)
    {
        return type.Contains('[') && type.EndsWith(']') && !IsSlice(type);
    }

    public static int ArrayLength(string arrayType)
    {
        var open = arrayType.LastIndexOf('[');
        return int.TryParse(arrayType[(open + 1)..^1], out var length) ? length : 0;
    }
}
