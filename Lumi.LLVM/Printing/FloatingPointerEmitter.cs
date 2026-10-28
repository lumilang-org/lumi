using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

internal sealed class FloatingPointPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    public void Emit(LLVMValueRef value, bool appendNewline = false)
    {
        var i8 = context.Int8Type;
        var i32 = context.Int32Type;
        var i64 = context.Int64Type;
        var doubleType = context.DoubleType;

        // Reserve a buffer for the formatted number.
        const int bufferSize = 128;
        var bufferType = LLVMTypeRef.CreateArray(i8, bufferSize);
        var buffer = builder.BuildAlloca(bufferType, "float_buffer");

        var zero = LLVMValueRef.CreateConstInt(i32, 0);
        var bufferPointer = builder.BuildGEP2(
            bufferType,
            buffer,
            new[] { zero, zero },
            "float_buffer_ptr");

        // Promote f16/f32 to double for the variadic C function.
        var promoted = value.TypeOf.Kind switch
        {
            LLVMTypeKind.LLVMHalfTypeKind =>
                builder.BuildFPExt(value, doubleType, "float_promote"),

            LLVMTypeKind.LLVMFloatTypeKind =>
                builder.BuildFPExt(value, doubleType, "float_promote"),

            LLVMTypeKind.LLVMDoubleTypeKind => value,

            _ => throw new NotSupportedException(
                $"Floating-point printing does not support type '{value.TypeOf}'.")
        };

        var formatText = appendNewline ? "%.15g\n\0" : "%.15g\0";
        var formatGlobal = builder.BuildGlobalStringPtr(
            formatText,
            "float_format");

        var (snprintfFunction, snprintfType) =
            WriterUtils.GetOrInsertSnprintf(module, context);

        var written = builder.BuildCall2(
            snprintfType,
            snprintfFunction,
            new[]
            {
                bufferPointer,
                LLVMValueRef.CreateConstInt(i64, bufferSize),
                formatGlobal,
                promoted
            },
            "float_snprintf");

        // snprintf returns the number of bytes that would have been written.
        // Clamp the write length to the actual buffer capacity minus the null byte.
        var maxLength = LLVMValueRef.CreateConstInt(i32, bufferSize - 1);

        var exceedsBuffer = builder.BuildICmp(
            LLVMIntPredicate.LLVMIntUGT,
            written,
            maxLength,
            "float_length_exceeds_buffer");

        var length = builder.BuildSelect(
            exceedsBuffer,
            maxLength,
            written,
            "float_output_length");

        var (writeFunction, writeType) =
            WriterUtils.GetOrInsertWrite(module, context);

        builder.BuildCall2(
            writeType,
            writeFunction,
            new[]
            {
                LLVMValueRef.CreateConstInt(i32, 1),
                bufferPointer,
                builder.BuildZExt(length, i64, "float_length_i64")
            },
            "float_write");
    }
}