namespace Lumi.Core.Semantics;

/// <summary>
///     Helpers for tuple type names. A tuple type is written <c>(T1, T2, ...)</c> with at least
///     two elements; <c>(T)</c> is only grouping and never a type.
/// </summary>
public static class TupleTypes
{
    public static bool IsTuple(string type)
    {
        return type.Length > 2 && type[0] == '(' && type[^1] == ')';
    }

    public static string Make(IEnumerable<string> elements)
    {
        return $"({string.Join(", ", elements)})";
    }

    public static IReadOnlyList<string> Elements(string tupleType)
    {
        return SplitTopLevel(tupleType[1..^1]);
    }

    // True when the text has a comma outside any (), [] nesting.
    public static bool HasTopLevelComma(string text)
    {
        return SplitTopLevel(text).Count > 1;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }
}
