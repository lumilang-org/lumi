using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Ast.Declarations;

public sealed class StructDeclarationNode(
    string name,
    IReadOnlyList<StructFieldNode> fields,
    IReadOnlyList<FunctionDeclarationNode> methods,
    SourceSpan span,
    VisibilityModifier visibilityModifier = VisibilityModifier.Private,
    IReadOnlyList<string>? typeParameters = null,
    IReadOnlyList<TypeConstraintNode>? constraints = null) : ASTNode(span)
{
    public IReadOnlyList<string> TypeParameters { get; } = typeParameters ?? [];
    public IReadOnlyList<TypeConstraintNode> Constraints { get; } = constraints ?? [];
    public string Name { get; } = name;
    public IReadOnlyList<StructFieldNode> Fields { get; } = fields;

    // Functions declared inside the struct body. They are not part of its memory layout.
    public IReadOnlyList<FunctionDeclarationNode> Methods { get; } = methods;
    public VisibilityModifier VisibilityModifier { get; } = visibilityModifier;
}
