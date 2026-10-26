using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

internal sealed class CharacterPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    public void Emit(LLVMValueRef valueToPrint)
    {
        var (writeFunction, writeFunctionType) =
            WriterUtils.GetOrInsertWrite(module, context);

        var stdout = LLVMValueRef.CreateConstInt(context.Int32Type, 1);
        var length = LLVMValueRef.CreateConstInt(context.Int64Type, 1);

        var characterPointer = builder.BuildAlloca(
            context.Int8Type, "char_print_ptr");

        builder.BuildStore(valueToPrint, characterPointer);

        builder.BuildCall2(
            writeFunctionType,
            writeFunction,
            new[] { stdout, characterPointer, length },
            "char_write_call");
    }
}