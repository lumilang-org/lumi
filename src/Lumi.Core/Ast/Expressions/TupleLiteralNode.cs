using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

// (a, b, ...): two or more elements.
public sealed class TupleLiteralNode(
    IReadOnlyList<ExpressionNode> elements,
    SourceSpan span) : ExpressionNode(span)
{
    public IReadOnlyList<ExpressionNode> Elements { get; } = elements;
}
