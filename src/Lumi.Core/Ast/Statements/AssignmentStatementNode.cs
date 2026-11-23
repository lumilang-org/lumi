using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class AssignmentStatementNode(
    string name,
    ExpressionNode value,
    SourceSpan span) : StatementNode(span)
{
    public string Name { get; } = name;

    public ExpressionNode Value { get; } = value;
}
