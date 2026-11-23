using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

/// <summary>Prints the address held by a pointer as <c>0x</c> followed by lowercase hex digits.</summary>
internal sealed class PointerPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    public void Emit(LLVMValueRef pointer)
    {
        var i8 = context.Int8Type;
        var i32 = context.Int32Type;
        var i64 = context.Int64Type;

        // "0x" + at most 16 hex digits + NUL fits comfortably.
        const int bufferSize = 32;
        var bufferType = LLVMTypeRef.CreateArray(i8, bufferSize);
        var buffer = builder.BuildAlloca(bufferType, "ptr_buffer");

        var zero = LLVMValueRef.CreateConstInt(i32, 0);
        var bufferPointer = builder.BuildGEP2(
            bufferType,
            buffer,
            new[] { zero, zero },
            "ptr_buffer_ptr");

        // Print the address itself (never the pointee), as a pointer-width integer.
        var address = builder.BuildPtrToInt(pointer, i64, "ptr_address");

        var formatGlobal = builder.BuildGlobalStringPtr("0x%llx\0", "ptr_format");

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
                address
            },
            "ptr_snprintf");

        var (writeFunction, writeType) =
            WriterUtils.GetOrInsertWrite(module, context);

        builder.BuildCall2(
            writeType,
            writeFunction,
            new[]
            {
                LLVMValueRef.CreateConstInt(i32, 1),
                bufferPointer,
                builder.BuildZExt(written, i64, "ptr_length_i64")
            },
            "ptr_write");
    }
}
