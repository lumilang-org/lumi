using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

// `target[start..end]`: a half-open range of a slice or array. An omitted bound is null
// (start defaults to 0, end to the length).
public sealed class RangeIndexExpressionNode(
    ExpressionNode target,
    ExpressionNode? start,
    ExpressionNode? end,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode? Start { get; } = start;
    public ExpressionNode? End { get; } = end;
}
