using Lumi.Core.Ast;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public static class InferBoundsUtils
{
    public static string InferNumericBounds(LiteralNode<string> num)
    {
        long.TryParse(num.Value, out var value);

        switch (value)
        {
            // Check against standard integer bounds from smallest to largest
            case >= sbyte.MinValue and <= sbyte.MaxValue:
            // Fits in a 16-bit signed integer
            case >= short.MinValue and <= short.MaxValue:
                return TypeUtils.I16; // Fits in an 8-bit signed integer
            case >= int.MinValue and <= int.MaxValue:
                return TypeUtils.I32;
            case >= long.MinValue and <= long.MaxValue:
                return TypeUtils.I64;
        }
    }
}