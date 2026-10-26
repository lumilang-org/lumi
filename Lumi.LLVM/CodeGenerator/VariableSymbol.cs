using LLVMSharp.Interop;

namespace Lumi.LLVM.CodeGenerator;

public sealed class VariableSymbol(
    string typeName,
    LLVMTypeRef llvmType,
    LLVMValueRef allocation)
{
    public string TypeName { get; } = typeName;

    public LLVMTypeRef LLVMType { get; } = llvmType;

    public LLVMValueRef Allocation { get; } = allocation;

    public bool IsUnsignedIntegerType()
    {
        return TypeName is "u8" or "u16" or "u32" or "u64";
    }
}