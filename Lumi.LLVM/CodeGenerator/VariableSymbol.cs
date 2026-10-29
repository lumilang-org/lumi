using LLVMSharp.Interop;
using Lumi.Core.Syntax;

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
        return TypeName is TypeConst.U8 or TypeConst.U16 or TypeConst.U32 or TypeConst.U64;
    }
}
