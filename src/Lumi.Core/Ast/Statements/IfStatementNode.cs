using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class IfStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> thenBody,
    SourceSpan span,
    IReadOnlyList<StatementNode>? elseBody = null)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> ThenBody { get; } = thenBody;

    public IReadOnlyList<StatementNode>? ElseBody { get; } = elseBody;
}
