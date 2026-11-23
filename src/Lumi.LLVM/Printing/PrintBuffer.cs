using LLVMSharp.Interop;

namespace Lumi.LLVM.Printing;

internal sealed class PrintBuffer
{
    private readonly LLVMTypeRef _arrayType;
    private readonly LLVMValueRef _allocation;
    private readonly LLVMContextRef _context;
    private readonly LLVMBuilderRef _builder;

    // Room for the 20 digits of a 64-bit value plus a sign.
    public const int Size = 32;

    private PrintBuffer(
        LLVMTypeRef arrayType,
        LLVMValueRef allocation,
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        _arrayType = arrayType;
        _allocation = allocation;
        _context = context;
        _builder = builder;
    }

    public static PrintBuffer Create(
        LLVMContextRef context,
        LLVMBuilderRef builder)
    {
        var arrayType = LLVMTypeRef.CreateArray(
            context.Int8Type,
            Size);

        var allocation = builder.BuildAlloca(
            arrayType,
            "print_buf");

        var buffer = new PrintBuffer(
            arrayType,
            allocation,
            context,
            builder);

        builder.BuildStore(
            LLVMValueRef.CreateConstInt(
                context.Int8Type,
                (ulong)'\n'),
            buffer.BytePointer(Size - 1, "newline_ptr"));

        return buffer;
    }

    public LLVMValueRef BytePointer(
        int index,
        string name)
    {
        return _builder.BuildInBoundsGEP2(
            _arrayType,
            _allocation,
            new[]
            {
                LLVMValueRef.CreateConstInt(
                    _context.Int32Type, 0),

                LLVMValueRef.CreateConstInt(
                    _context.Int32Type, (ulong)index)
            },
            name);
    }
}