using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class WhileStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> Body { get; } = body;
}
