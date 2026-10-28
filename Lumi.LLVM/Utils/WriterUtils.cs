using System.Text;
using LLVMSharp.Interop;
using Lumi.LLVM.Printing;

namespace Lumi.LLVM.Utils;

public static class WriterUtils
{
    internal static (LLVMValueRef Func, LLVMTypeRef FuncType) GetOrInsertWrite(LLVMModuleRef module,
        LLVMContextRef context)
    {
        var bytePointerType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        var functionType = LLVMTypeRef.CreateFunction(
            context.Int64Type,
            new[]
            {
                context.Int32Type,
                bytePointerType,
                context.Int64Type
            });

        var function = module.GetNamedFunction("write");

        if (function.Handle != IntPtr.Zero)
            return (function, functionType);

        function = module.AddFunction("write", functionType);

        return (function, functionType);
    }

    public static (LLVMValueRef Function, LLVMTypeRef Type) GetOrInsertSnprintf(
        LLVMModuleRef module,
        LLVMContextRef context)
    {
        var i8 = context.Int8Type;
        var i32 = context.Int32Type;
        var i64 = context.Int64Type;
        var bytePointerType = LLVMTypeRef.CreatePointer(i8, 0);

        // int snprintf(char *str, size_t size, const char *format, ...);
        var functionType = LLVMTypeRef.CreateFunction(
            i32,
            new[]
            {
                bytePointerType,
                i64,
                bytePointerType
            },
            true);

        var function = module.GetNamedFunction("snprintf");

        if (function.Handle == IntPtr.Zero) function = module.AddFunction("snprintf", functionType);

        return (function, functionType);
    }

    internal static void EmitPrintInt(
        LLVMValueRef valueToPrint,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        new IntegerPrintEmitter(module, context, builder)
            .Emit(valueToPrint);
    }

    internal static void EmitPrintChar(
        LLVMValueRef valueToPrint,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        new CharacterPrintEmitter(module, context, builder)
            .Emit(valueToPrint);
    }

    internal static void EmitSystemWrite(
        string message,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var (function, functionType) = GetOrInsertWrite(
            module, context);

        var messagePointer = builder.BuildGlobalStringPtr(
            message, "str_lit");

        var stdout = LLVMValueRef.CreateConstInt(
            context.Int32Type, 1);

        var length = LLVMValueRef.CreateConstInt(
            context.Int64Type,
            (ulong)Encoding.UTF8.GetByteCount(message));

        builder.BuildCall2(
            functionType,
            function,
            new[] { stdout, messagePointer, length },
            "write_call");
    }

    internal static void EmitPrintBool(LLVMValueRef printValue, LLVMModuleRef module, LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        new BooleanPrintEmitter(module, context, builder)
            .Emit(printValue);
    }

    internal static void EmitPrintFloat(LLVMValueRef printValue, LLVMModuleRef module, LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        new FloatingPointPrintEmitter(module, context, builder)
            .Emit(printValue);
    }
}