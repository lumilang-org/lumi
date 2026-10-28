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
                return TypeConst.I16; // Fits in an 8-bit signed integer
            case >= int.MinValue and <= int.MaxValue:
                return TypeConst.I32;
            case >= long.MinValue and <= long.MaxValue:
                return TypeConst.I64;
        }
    }

    public static string InferFloating(LiteralNode<double> num)
    {
        if (!double.IsFinite(num.Value))
            throw new Exception(
                $"Invalid floating-point literal: '{num.Value}'.");

        // Default type for floating-point literals.
        return TypeConst.F64;
    }
}