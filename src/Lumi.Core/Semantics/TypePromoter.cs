using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public static class TypePromoter
{
    private const string DefaultIntegerType = TypeConst.I32;

    public static string? GetCommonType(string leftType, string rightType)
    {
        if (!IsIntegerType(leftType) || !IsIntegerType(rightType))
        {
            return null;
        }

        // usize is its own type: it combines with itself and with narrower unsigned types
        // (which widen to it), but not with u64 or any signed type.
        if (leftType == TypeConst.Usize || rightType == TypeConst.Usize)
        {
            var other = leftType == TypeConst.Usize ? rightType : leftType;

            return other is TypeConst.Usize or TypeConst.U8 or TypeConst.U16 or TypeConst.U32
                ? TypeConst.Usize
                : null;
        }

        var leftIsUnsigned = IsUnsignedIntegerType(leftType);
        var rightIsUnsigned = IsUnsignedIntegerType(rightType);

        if (leftIsUnsigned != rightIsUnsigned)
        {
            return null;
        }

        var leftWidth = GetIntegerWidth(leftType);
        var rightWidth = GetIntegerWidth(rightType);
        var widestWidth = Math.Max(leftWidth, rightWidth);

        // Apply integer promotion after determining the common width.
        if (widestWidth < 32)
        {
            return DefaultIntegerType;
        }

        return $"{(leftIsUnsigned ? "u" : "i")}{widestWidth}";
    }

    public static bool IsIntegerType(string type)
    {
        return type is
            "i8" or "i16" or "i32" or "i64" or
            "u8" or "u16" or "u32" or "u64" or "usize";
    }

    private static bool IsUnsignedIntegerType(string type)
    {
        return type.StartsWith('u');
    }

    private static int GetIntegerWidth(string type)
    {
        return type switch
        {
            TypeConst.I8 or TypeConst.U8 => 8,
            TypeConst.I16 or TypeConst.U16 => 16,
            TypeConst.I32 or TypeConst.U32 => 32,
            // usize is pointer-sized; the checker assumes a 64-bit target.
            TypeConst.I64 or TypeConst.U64 or TypeConst.Usize => 64,
            _ => throw new ArgumentException(
                $"'{type}' is not a supported integer type.",
                nameof(type))
        };
    }
}
