using LLVMSharp.Interop;
using Lumi.Core.Ast;

namespace Lumi.LLVM.Utils;

public static class NumberConverterUtils
{
    public static LLVMTypeRef TranslateIntLiteral(NumberLiteralNode node)
    {
        if (!long.TryParse(node.Value, out var value))
        {
            throw new OverflowException(
                $"Integer literal '{node.Value}' exceeds the supported range.");
        }

        if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
            return LLVMTypeRef.Int8;

        if (value >= short.MinValue && value <= short.MaxValue)
            return LLVMTypeRef.Int16;

        if (value >= int.MinValue && value <= int.MaxValue)
            return LLVMTypeRef.Int32;

        return LLVMTypeRef.Int64;
    }
}