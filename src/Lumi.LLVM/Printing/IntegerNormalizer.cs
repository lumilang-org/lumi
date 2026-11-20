using LLVMSharp.Interop;

namespace Lumi.LLVM.Printing;

internal static class IntegerNormalizer
{
    public static LLVMValueRef NormalizeToSupportedInteger(
        LLVMValueRef value)
    {
        var type = value.TypeOf;

        if (type.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            throw new NotSupportedException(
                $"Expected an integer, got {type}.");
        }

        return type.IntWidth switch
        {
            8 or 16 or 32 or 64 => value,

            var width => throw new NotSupportedException(
                $"Integer printing does not support i{width}.")
        };
    }
}
