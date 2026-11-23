using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class ForStatementNode(
    StatementNode? initializer,
    ExpressionNode? condition,
    StatementNode? update,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public StatementNode? Initializer { get; } = initializer;
    public ExpressionNode? Condition { get; } = condition;
    public StatementNode? Update { get; } = update;
    public IReadOnlyList<StatementNode> Body { get; } = body;
}
