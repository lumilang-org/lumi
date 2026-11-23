using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public static class InferBoundsUtils
{
    public static string InferNumericBounds(LiteralNode<string> num)
    {
        if (!long.TryParse(num.Value, out var value))
        {
            throw new Exception(
                $"Integer literal is outside the supported range: '{num.Value}'.");
        }

        return value switch
        {
            >= sbyte.MinValue and <= sbyte.MaxValue => TypeConst.I8,
            >= short.MinValue and <= short.MaxValue => TypeConst.I16,
            >= int.MinValue and <= int.MaxValue => TypeConst.I32,
            _ => TypeConst.I64
        };
    }


    public static string InferFloating(LiteralNode<double> num)
    {
        if (!double.IsFinite(num.Value))
        {
            throw new Exception(
                $"Invalid floating-point literal: '{num.Value}'.");
        }

        // Default type for floating-point literals.
        return TypeConst.F64;
    }
}
