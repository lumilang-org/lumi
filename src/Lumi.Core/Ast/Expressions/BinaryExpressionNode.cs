using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class BinaryExpressionNode(
    ExpressionNode left,
    string op,
    ExpressionNode right,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Left { get; } = left;
    public string Operator { get; } = op;
    public ExpressionNode Right { get; } = right;
}
