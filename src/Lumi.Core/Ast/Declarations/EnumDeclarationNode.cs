using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Ast.Declarations;

public enum EnumVariantKind
{
    Unit,
    Tuple,
    Struct
}

// A payload field of a variant. Tuple variant fields have no name (Name is null).
public sealed class EnumVariantFieldNode(string? name, string type, SourceSpan span) : ASTNode(span)
{
    public string? Name { get; } = name;
    public string Type { get; } = type;
}

public sealed class EnumVariantNode(
    string name,
    EnumVariantKind kind,
    IReadOnlyList<EnumVariantFieldNode> fields,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public EnumVariantKind Kind { get; } = kind;
    public IReadOnlyList<EnumVariantFieldNode> Fields { get; } = fields;
}

public sealed class EnumDeclarationNode(
    string name,
    IReadOnlyList<EnumVariantNode> variants,
    SourceSpan span,
    VisibilityModifier visibilityModifier = VisibilityModifier.Private,
    IReadOnlyList<string>? typeParameters = null,
    IReadOnlyList<TypeConstraintNode>? constraints = null) : ASTNode(span)
{
    public IReadOnlyList<string> TypeParameters { get; } = typeParameters ?? [];
    public IReadOnlyList<TypeConstraintNode> Constraints { get; } = constraints ?? [];

    // For a specialization of a generic enum: the template's name (`Option` for `Option__i32`).
    public string? GenericOrigin { get; private set; }
    public string Name { get; } = name;
    public IReadOnlyList<EnumVariantNode> Variants { get; } = variants;
    public VisibilityModifier VisibilityModifier { get; } = visibilityModifier;
}
