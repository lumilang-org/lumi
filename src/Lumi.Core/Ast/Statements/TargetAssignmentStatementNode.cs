using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

// Generalize assignment target so both `a = 1` and `p.x = 1` work
public sealed class TargetAssignmentStatementNode(
    ExpressionNode target,
    ExpressionNode value,
    SourceSpan span) : StatementNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode Value { get; } = value;
}
