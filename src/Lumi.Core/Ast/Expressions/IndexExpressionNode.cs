using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class IndexExpressionNode(
    ExpressionNode target,
    ExpressionNode index,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode Index { get; } = index;
}
