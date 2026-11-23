using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class ReturnStatementNode(ExpressionNode? expression, SourceSpan span) : StatementNode(span)
{
    public ExpressionNode? Expression { get; } = expression;
}
