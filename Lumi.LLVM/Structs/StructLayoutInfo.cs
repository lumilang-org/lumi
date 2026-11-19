using LLVMSharp.Interop;

namespace Lumi.LLVM.Structs;

internal sealed record StructFieldLayout(
    int Index,
    string Name,
    string LumiType,
    LLVMTypeRef LlvmType);

internal sealed record StructLayoutInfo(
    string Name,
    LLVMTypeRef StructType,
    IReadOnlyList<StructFieldLayout> Fields,
    IReadOnlyDictionary<string, StructFieldLayout> FieldsByName);
