using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class StructLiteralNode(
    string structName,
    IReadOnlyList<StructFieldInitNode> initializers,
    SourceSpan span) : ExpressionNode(span)
{
    public string StructName { get; } = structName;
    public IReadOnlyList<StructFieldInitNode> Initializers { get; } = initializers;
}
