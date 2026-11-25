using LLVMSharp.Interop;
using Lumi.Core.Ast.Declarations;

namespace Lumi.LLVM.Structs;

internal sealed record EnumVariantLayout(
    string Name,
    int Tag,
    EnumVariantKind Kind,
    IReadOnlyList<string?> FieldNames,
    IReadOnlyList<string> FieldTypes,
    // The payload as a plain struct of the variant's fields; empty for unit variants.
    LLVMTypeRef PayloadType);

// An enum is { i32 tag, [N x iA] payload }: N * A bytes cover the largest variant and A is the
// largest variant alignment, so the payload sits at the same offset for every variant.
internal sealed record EnumLayoutInfo(
    string Name,
    LLVMTypeRef Type,
    EnumDeclarationNode Declaration,
    IReadOnlyDictionary<string, EnumVariantLayout> Variants);
