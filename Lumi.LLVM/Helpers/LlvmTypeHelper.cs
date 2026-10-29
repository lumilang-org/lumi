using LLVMSharp.Interop;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.LLVM.Helpers;

public sealed class LlvmTypeHelper(
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    public LLVMTypeRef GetIntegerType(string typeName)
    {
        var width = GetIntegerWidth(typeName);

        return width switch
        {
            8 => context.Int8Type,
            16 => context.Int16Type,
            32 => context.Int32Type,
            64 => context.Int64Type,

            _ => throw new ArgumentOutOfRangeException(nameof(typeName))
        };
    }

    private static int GetIntegerWidth(string typeName)
    {
        return typeName switch
        {
            TypeConst.I8 or TypeConst.U8 => 8,
            TypeConst.I16 or TypeConst.U16 => 16,
            TypeConst.I32 or TypeConst.U32 => 32,
            TypeConst.I64 or TypeConst.U64 => 64,

            _ => throw new ArgumentException(
                $"'{typeName}' is not a supported integer type.",
                nameof(typeName))
        };
    }

    public LLVMValueRef ConvertInteger(
        LLVMValueRef value,
        string sourceType,
        string targetType)
    {
        var sourceWidth = GetIntegerWidth(sourceType);
        var targetWidth = GetIntegerWidth(targetType);

        if (sourceWidth == targetWidth)
        {
            return value;
        }

        var targetLlvmType = GetIntegerType(targetType);

        if (sourceWidth > targetWidth)
        {
            return builder.BuildTrunc(
                value,
                targetLlvmType,
                "int.trunc");
        }

        return TypeChecker.IsUnsignedInteger(sourceType)
            ? builder.BuildZExt(
                value,
                targetLlvmType,
                "int.zext")
            : builder.BuildSExt(
                value,
                targetLlvmType,
                "int.sext");
    }
}
