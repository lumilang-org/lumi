namespace Lumi.LLVM.CodeGenerator;

using LLVMSharp.Interop;

public static class WriterUtils
{
    private static (LLVMValueRef Func, LLVMTypeRef FuncType) GetOrInsertWrite(LLVMModuleRef module, LLVMContextRef context)
    {
        var voidPtrType = LLVMTypeRef.CreatePointer(context.Int8Type, 0);
        var writeType = LLVMTypeRef.CreateFunction(
            context.Int64Type, 
            new LLVMTypeRef[] { context.Int32Type, voidPtrType, context.Int64Type }
        );

        var writeFunc = module.GetNamedFunction("write");
        if (writeFunc.Handle != IntPtr.Zero)
        {
            return (writeFunc, writeType);
        }
    
        var addedFunc = module.AddFunction("write", writeType);
        return (addedFunc, writeType);
    }
    
    public static void EmitSystemWrite(string message, LLVMModuleRef module, LLVMContextRef context, LLVMBuilderRef builder)
    {
        var (writeFn, writeType) = GetOrInsertWrite(module, context);

        var messageGlobal = builder.BuildGlobalStringPtr(message, "str_lit");
        var stdoutFd = LLVMValueRef.CreateConstInt(context.Int32Type, 1);
        var length = (ulong)System.Text.Encoding.UTF8.GetByteCount(message);
        var lenValue = LLVMValueRef.CreateConstInt(context.Int64Type, length);

        builder.BuildCall2(
            writeType,
            writeFn, 
            new LLVMValueRef[] { stdoutFd, messageGlobal, lenValue },
            "write_call"
        );
    }
    
    public static void EmitPrintInt(LLVMValueRef valueToPrint, LLVMModuleRef module, LLVMContextRef context, LLVMBuilderRef builder)
    {
        // Unpack both the function reference and its explicit type signature
        var (writeFn, writeType) = GetOrInsertWrite(module, context);
        var stdoutFd = LLVMValueRef.CreateConstInt(context.Int32Type, 1);

        if (valueToPrint.TypeOf == context.Int16Type)
        {
            valueToPrint = builder.BuildSExt(valueToPrint, context.Int32Type, "val_i32");
        }

        // 2. Allocate a 16-byte buffer on the stack for digits + newline
        var bufferSize = 16;
        var bufferType = LLVMTypeRef.CreateArray(context.Int8Type, (uint)bufferSize);
        var bufAlloca = builder.BuildAlloca(bufferType, "print_buf");

        // 3. Place a newline character at the very end of the buffer
        var nlCharPtr = builder.BuildInBoundsGEP2(
            bufferType,
            bufAlloca,
            new LLVMValueRef[] { 
                LLVMValueRef.CreateConstInt(context.Int32Type, 0), 
                LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)(bufferSize - 1)) 
            },
            "nl_char_ptr"
        );
        builder.BuildStore(LLVMValueRef.CreateConstInt(context.Int8Type, 10), nlCharPtr); // ASCII 10 = '\n'

        // Track pointer iterator and remaining value using stack variables
        var ptrAlloca = builder.BuildAlloca(LLVMTypeRef.CreatePointer(context.Int8Type, 0), "ptr_iter");
        var valAlloca = builder.BuildAlloca(context.Int32Type, "val_iter");

        // Start pointer right before the newline position
        var initialPtr = builder.BuildInBoundsGEP2(
            bufferType,
            bufAlloca,
            new LLVMValueRef[] { 
                LLVMValueRef.CreateConstInt(context.Int32Type, 0), 
                LLVMValueRef.CreateConstInt(context.Int32Type, (ulong)(bufferSize - 2)) 
            },
            "initial_ptr"
        );
        builder.BuildStore(initialPtr, ptrAlloca);
        builder.BuildStore(valueToPrint, valAlloca);

        var function = builder.InsertBlock.Parent;
        var loopBlock = context.AppendBasicBlock(function, "itoa_loop");
        var afterBlock = context.AppendBasicBlock(function, "itoa_after");

        builder.BuildBr(loopBlock);

        // 4. Emit the do-while conversion loop
        builder.PositionAtEnd(loopBlock);
        var curVal = builder.BuildLoad2(context.Int32Type, valAlloca, "cur_val");
        var curPtr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int8Type, 0), ptrAlloca, "cur_ptr");

        // digit = curVal % 10
        var digit = builder.BuildSRem(curVal, LLVMValueRef.CreateConstInt(context.Int32Type, 10), "digit");
        // ascii = digit + 48 ('0')
        var ascii = builder.BuildAdd(digit, LLVMValueRef.CreateConstInt(context.Int32Type, 48), "ascii");
        var asciiChar = builder.BuildTrunc(ascii, context.Int8Type, "ascii_char");

        builder.BuildStore(asciiChar, curPtr);

        // nextVal = curVal / 10
        var nextVal = builder.BuildSDiv(curVal, LLVMValueRef.CreateConstInt(context.Int32Type, 10), "next_val");
        builder.BuildStore(nextVal, valAlloca);

        // nextPtr = curPtr - 1
        var nextPtr = builder.BuildInBoundsGEP2(
            context.Int8Type,
            curPtr,
            new LLVMValueRef[] { LLVMValueRef.CreateConstInt(context.Int32Type, unchecked((ulong)-1)) },
            "next_ptr"
        );
        builder.BuildStore(nextPtr, ptrAlloca);

        // Continue looping while nextVal > 0
        var continueCond = builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, nextVal, LLVMValueRef.CreateConstInt(context.Int32Type, 0), "cond");
        builder.BuildCondBr(continueCond, loopBlock, afterBlock);

        // 5. Compute final buffer length and execute write syscall
        builder.PositionAtEnd(afterBlock);
        
        var finalPtr = builder.BuildLoad2(LLVMTypeRef.CreatePointer(context.Int8Type, 0), ptrAlloca, "load_final_ptr");
        var startPtr = builder.BuildInBoundsGEP2(
            context.Int8Type,
            finalPtr,
            new LLVMValueRef[] { LLVMValueRef.CreateConstInt(context.Int32Type, 1) },
            "start_ptr"
        );

        var startInt = builder.BuildPtrToInt(startPtr, context.Int64Type, "start_int");
        var nlInt = builder.BuildPtrToInt(nlCharPtr, context.Int64Type, "nl_int");
        
        var diff = builder.BuildSub(nlInt, startInt, "diff");
        var length = builder.BuildAdd(diff, LLVMValueRef.CreateConstInt(context.Int64Type, 1), "total_len");

        builder.BuildCall2(
            writeType,
            writeFn,
            new LLVMValueRef[] { stdoutFd, startPtr, length },
            "write_call"
        );
    }
}