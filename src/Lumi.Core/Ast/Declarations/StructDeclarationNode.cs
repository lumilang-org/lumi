using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class StructDeclarationNode(
    string name,
    IReadOnlyList<StructFieldNode> fields,
    IReadOnlyList<FunctionDeclarationNode> methods,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<StructFieldNode> Fields { get; } = fields;

    // Functions declared inside the struct body. They are not part of its memory layout.
    public IReadOnlyList<FunctionDeclarationNode> Methods { get; } = methods;
}
