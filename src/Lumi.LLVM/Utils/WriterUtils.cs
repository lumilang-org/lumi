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
        LLVMBuilderRef builder,
        bool isUnsigned = false)
    {
        new IntegerPrintEmitter(module, context, builder)
            .Emit(valueToPrint, isUnsigned);
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

    internal static (LLVMValueRef Function, LLVMTypeRef Type) GetOrInsertStrlen(
        LLVMModuleRef module,
        LLVMContextRef context)
    {
        var bytePointerType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        // size_t strlen(const char *s);
        var functionType = LLVMTypeRef.CreateFunction(
            context.Int64Type,
            new[] { bytePointerType });

        var function = module.GetNamedFunction("strlen");

        if (function.Handle == IntPtr.Zero) function = module.AddFunction("strlen", functionType);

        return (function, functionType);
    }

    internal static (LLVMValueRef Function, LLVMTypeRef Type) GetOrInsertStrcmp(
        LLVMModuleRef module,
        LLVMContextRef context)
    {
        var bytePointerType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        // int strcmp(const char *a, const char *b);
        var functionType = LLVMTypeRef.CreateFunction(
            context.Int32Type,
            new[] { bytePointerType, bytePointerType });

        var function = module.GetNamedFunction("strcmp");

        if (function.Handle == IntPtr.Zero) function = module.AddFunction("strcmp", functionType);

        return (function, functionType);
    }

    /// <summary>Emits a call to strlen and returns its i64 result.</summary>
    internal static LLVMValueRef EmitStringLength(
        LLVMValueRef stringPointer,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var (strlen, strlenType) = GetOrInsertStrlen(module, context);

        return builder.BuildCall2(strlenType, strlen, new[] { stringPointer }, "str_len");
    }

    /// <summary>Emits a call to strcmp and returns its i32 result (0 when equal).</summary>
    internal static LLVMValueRef EmitStringCompare(
        LLVMValueRef left,
        LLVMValueRef right,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var (strcmp, strcmpType) = GetOrInsertStrcmp(module, context);

        return builder.BuildCall2(strcmpType, strcmp, new[] { left, right }, "str_cmp");
    }

    internal static void EmitPrintString(
        LLVMValueRef stringPointer,
        LLVMModuleRef module,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var (write, writeType) = GetOrInsertWrite(module, context);
        var length = EmitStringLength(stringPointer, module, context, builder);

        var stdout = LLVMValueRef.CreateConstInt(context.Int32Type, 1);

        builder.BuildCall2(
            writeType,
            write,
            new[] { stdout, stringPointer, length },
            "write_call");
    }

    internal static void EmitPrintPointer(LLVMValueRef pointer, LLVMModuleRef module, LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        new PointerPrintEmitter(module, context, builder)
            .Emit(pointer);
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