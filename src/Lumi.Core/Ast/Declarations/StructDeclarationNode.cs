using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class StructDeclarationNode(
    string name,
    IReadOnlyList<StructFieldNode> fields,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<StructFieldNode> Fields { get; } = fields;
}
