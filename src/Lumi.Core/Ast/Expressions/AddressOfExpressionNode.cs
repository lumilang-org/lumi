using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

/// <summary>Address-of, <c>&amp;operand</c>: yields a pointer to an addressable operand.</summary>
public sealed class AddressOfExpressionNode(
    ExpressionNode operand,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Operand { get; } = operand;
}
