using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class UnaryExpressionNode(
    string @operator,
    ExpressionNode operand,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Operator { get; } = @operator;
    public ExpressionNode Operand { get; } = operand;
}
