using LLVMSharp.Interop;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.Printing;

internal sealed class IntegerPrintEmitter(
    LLVMModuleRef module,
    LLVMContextRef context,
    LLVMBuilderRef builder)
{
    private const int StdoutFileDescriptor = 1;

    public void Emit(LLVMValueRef value, bool isUnsigned = false)
    {
        var i8 = context.Int8Type;
        var i32 = context.Int32Type;
        var i64 = context.Int64Type;
        var bytePointerType = LLVMTypeRef.CreatePointer(i8, 0);

        var function = builder.InsertBlock.Parent;

        var normalized =
            IntegerNormalizer.NormalizeToSupportedInteger(value);

        var integerType = normalized.TypeOf;

        var zero = LLVMValueRef.CreateConstInt(integerType, 0);
        var ten = LLVMValueRef.CreateConstInt(integerType, 10);
        var one = LLVMValueRef.CreateConstInt(i32, 1);

        var buffer = PrintBuffer.Create(context, builder);

        var pointerSlot = builder.BuildAlloca(
            bytePointerType, "itoa_ptr");

        var valueSlot = builder.BuildAlloca(
            integerType, "itoa_value");

        var lastDigitPointer = buffer.BytePointer(
            PrintBuffer.Size - 1, "itoa_last_digit_ptr");

        builder.BuildStore(lastDigitPointer, pointerSlot);

        // Unsigned values never get a sign: all of their bits are magnitude.
        var isNegative = isUnsigned
            ? LLVMValueRef.CreateConstInt(context.Int1Type, 0)
            : builder.BuildICmp(
                LLVMIntPredicate.LLVMIntSLT,
                normalized,
                zero,
                "itoa_is_negative");

        // Negation wraps for the minimum signed integer.
        // The resulting bit pattern is handled using unsigned division.
        var magnitude = builder.BuildSelect(
            isNegative,
            builder.BuildSub(zero, normalized, "itoa_negated"),
            normalized,
            "itoa_magnitude");

        builder.BuildStore(magnitude, valueSlot);

        var loopBlock = context.AppendBasicBlock(
            function, "itoa_loop");

        var afterDigitsBlock = context.AppendBasicBlock(
            function, "itoa_after_digits");

        var signBlock = context.AppendBasicBlock(
            function, "itoa_sign");

        var outputBlock = context.AppendBasicBlock(
            function, "itoa_output");

        builder.BuildBr(loopBlock);

        // Generate decimal digits from right to left.
        builder.PositionAtEnd(loopBlock);

        var currentValue = builder.BuildLoad2(
            integerType, valueSlot, "itoa_current");

        var digit = builder.BuildURem(
            currentValue, ten, "itoa_digit");

        var nextValue = builder.BuildUDiv(
            currentValue, ten, "itoa_next");

        var asciiDigit = builder.BuildAdd(
            digit,
            LLVMValueRef.CreateConstInt(integerType, '0'),
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
            hasMoreDigits,
            loopBlock,
            afterDigitsBlock);

        // The pointer now points one byte before the first digit.
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
            isNegative,
            signBlock,
            outputBlock);

        // Write '-' before the digits for negative values.
        builder.PositionAtEnd(signBlock);

        builder.BuildStore(
            LLVMValueRef.CreateConstInt(i8, '-'),
            digitsStartMinusOne);

        builder.BuildBr(outputBlock);

        // The PHI must be the first instruction in this block.
        builder.PositionAtEnd(outputBlock);

        var outputStart = builder.BuildPhi(
            bytePointerType,
            "itoa_output_start");

        outputStart.AddIncoming(
            new[] { positiveStart, digitsStartMinusOne },
            new[] { afterDigitsBlock, signBlock },
            2);

        var startAddress = builder.BuildPtrToInt(
            outputStart, i64, "itoa_start_addr");

        var endAddress = builder.BuildPtrToInt(
            lastDigitPointer, i64, "itoa_end_addr");

        var length = builder.BuildAdd(
            builder.BuildSub(
                endAddress,
                startAddress,
                "itoa_length_without_last_digit"),
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

        // Leave the builder at itoa_output.
        // The caller emits subsequent instructions and terminates the block.
    }
}
