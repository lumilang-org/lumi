using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class ArrayLiteralNode(
    IReadOnlyList<ExpressionNode> elements,
    SourceSpan span) : ExpressionNode(span)
{
    public IReadOnlyList<ExpressionNode> Elements { get; } = elements;
}
