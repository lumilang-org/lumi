
using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

internal sealed class IntegerPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    private const int StdoutFileDescriptor = 1;

    public void Emit(LLVMValueRef value)
    {
        var i8 = context.Int8Type;
        var i32 = context.Int32Type;
        var i64 = context.Int64Type;
        var bytePointerType = LLVMTypeRef.CreatePointer(i8, 0);

        var normalized = IntegerNormalizer.NormalizeToInt32(
            value, context, builder);

        var buffer = PrintBuffer.Create(context, builder);
        var function = builder.InsertBlock.Parent;

        var zero = LLVMValueRef.CreateConstInt(i32, 0);
        var one = LLVMValueRef.CreateConstInt(i32, 1);
        var ten = LLVMValueRef.CreateConstInt(i32, 10);

        var pointerSlot = builder.BuildAlloca(bytePointerType, "itoa_ptr");
        var valueSlot = builder.BuildAlloca(i32, "itoa_value");

        var newlinePointer = buffer.BytePointer(
            PrintBuffer.Size - 1, "itoa_newline_ptr");

        var lastDigitPointer = buffer.BytePointer(
            PrintBuffer.Size - 2, "itoa_last_digit_ptr");

        builder.BuildStore(lastDigitPointer, pointerSlot);

        var isNegative = builder.BuildICmp(
            LLVMIntPredicate.LLVMIntSLT,
            normalized,
            zero,
            "itoa_is_negative");

        // Negation wraps for INT32_MIN. Unsigned division handles
        // the resulting bit pattern correctly.
        var magnitude = builder.BuildSelect(
            isNegative,
            builder.BuildSub(zero, normalized, "itoa_negated"),
            normalized,
            "itoa_magnitude");

        builder.BuildStore(magnitude, valueSlot);

        var loopBlock = context.AppendBasicBlock(function, "itoa_loop");
        var afterDigitsBlock = context.AppendBasicBlock(function, "itoa_after_digits");
        var signBlock = context.AppendBasicBlock(function, "itoa_sign");
        var outputBlock = context.AppendBasicBlock(function, "itoa_output");

        builder.BuildBr(loopBlock);

        // Generate digits from right to left.
        builder.PositionAtEnd(loopBlock);

        var currentValue = builder.BuildLoad2(
            i32, valueSlot, "itoa_current");

        var digit = builder.BuildURem(
            currentValue, ten, "itoa_digit");

        var nextValue = builder.BuildUDiv(
            currentValue, ten, "itoa_next");

        var asciiDigit = builder.BuildAdd(
            digit,
            LLVMValueRef.CreateConstInt(i32, (ulong)'0'),
            "itoa_digit_ascii");

        var digitByte = builder.BuildTrunc(
            asciiDigit, i8, "itoa_digit_i8");

        var currentPointer = builder.BuildLoad2(
            bytePointerType, pointerSlot, "itoa_current_ptr");

        builder.BuildStore(digitByte, currentPointer);

        var previousPointer = builder.BuildGEP2(
            i8,
            currentPointer,
            new[]
            {
                LLVMValueRef.CreateConstInt(i32, uint.MaxValue)
            },
            "itoa_previous_ptr");

        builder.BuildStore(previousPointer, pointerSlot);
        builder.BuildStore(nextValue, valueSlot);

        var hasMoreDigits = builder.BuildICmp(
            LLVMIntPredicate.LLVMIntUGT,
            nextValue,
            zero,
            "itoa_has_more_digits");

        builder.BuildCondBr(
            hasMoreDigits, loopBlock, afterDigitsBlock);

        // After the loop, pointerSlot points one byte before the first digit.
        builder.PositionAtEnd(afterDigitsBlock);

        var digitsStartMinusOne = builder.BuildLoad2(
            bytePointerType,
            pointerSlot,
            "itoa_digits_start_minus_one");

        var positiveStart = builder.BuildGEP2(
            i8,
            digitsStartMinusOne,
            new[] { one },
            "itoa_positive_start");

        builder.BuildCondBr(
            isNegative, signBlock, outputBlock);

        // Write '-' immediately before the digits for negative numbers.
        builder.PositionAtEnd(signBlock);

        builder.BuildStore(
            LLVMValueRef.CreateConstInt(i8, (ulong)'-'),
            digitsStartMinusOne);

        builder.BuildBr(outputBlock);

        // PHI must be the first instruction in this block.
        builder.PositionAtEnd(outputBlock);

        var outputStart = builder.BuildPhi(
            bytePointerType,
            "itoa_output_start");

        outputStart.AddIncoming(
            new[] { positiveStart, digitsStartMinusOne },
            new[] { afterDigitsBlock, signBlock },
            2);

        // Calculate the number of bytes from outputStart to the newline,
        // including the newline itself.
        var startAddress = builder.BuildPtrToInt(
            outputStart, i64, "itoa_start_addr");

        var endAddress = builder.BuildPtrToInt(
            newlinePointer, i64, "itoa_end_addr");

        var length = builder.BuildAdd(
            builder.BuildSub(
                endAddress, startAddress, "itoa_length_without_newline"),
            LLVMValueRef.CreateConstInt(i64, 1),
            "itoa_length");

        var (writeFunction, writeType) =
            WriterUtils.GetOrInsertWrite(module, context);

        var stdout = LLVMValueRef.CreateConstInt(
            i32, StdoutFileDescriptor);

        builder.BuildCall2(
            writeType,
            writeFunction,
            new[] { stdout, outputStart, length },
            "itoa_write");
    }
}
