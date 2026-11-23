using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

/// <summary>Dereference, <c>*operand</c>: the value a pointer points to. Also an assignable lvalue.</summary>
public sealed class DereferenceExpressionNode(
    ExpressionNode operand,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Operand { get; } = operand;
}
