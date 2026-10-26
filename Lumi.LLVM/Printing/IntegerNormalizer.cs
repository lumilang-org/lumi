using LLVMSharp.Interop;

namespace Lumi.LLVM.Printing;

internal static class IntegerNormalizer
{
    public static LLVMValueRef NormalizeToInt32(
        LLVMValueRef value,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var type = value.TypeOf;

        if (type.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            throw new NotSupportedException(
                $"Expected an integer, got {type}.");
        }

        return type.IntWidth switch
        {
            8 => builder.BuildSExt(
                value,
                context.Int32Type,
                "print_value_i32"),

            16 => builder.BuildSExt(
                value,
                context.Int32Type,
                "print_value_i32"),

            32 => value,

            var width => throw new NotSupportedException(
                $"Integer printing does not support i{width}.")
        };
    }
}
