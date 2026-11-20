using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

internal sealed class BooleanPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    public void Emit(LLVMValueRef valueToPrint)
    {
        var (writeFunction, writeFunctionType) =
            WriterUtils.GetOrInsertWrite(module, context);

        var trueString = builder.BuildGlobalStringPtr(
            "true", ".bool_true");

        var falseString = builder.BuildGlobalStringPtr(
            "false", ".bool_false");

        var selectedString = builder.BuildSelect(
            valueToPrint,
            trueString,
            falseString,
            "bool_string");

        var length = builder.BuildSelect(
            valueToPrint,
            LLVMValueRef.CreateConstInt(context.Int64Type, 4),
            LLVMValueRef.CreateConstInt(context.Int64Type, 5),
            "bool_length");

        var stdout = LLVMValueRef.CreateConstInt(
            context.Int32Type, 1);

        builder.BuildCall2(
            writeFunctionType,
            writeFunction,
            new[] { stdout, selectedString, length },
            "bool_write_call");
    }
}