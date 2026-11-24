using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class CastExpressionNode(ExpressionNode operand, string targetType, SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Operand { get; } = operand;
    public string TargetType { get; } = targetType;
}
