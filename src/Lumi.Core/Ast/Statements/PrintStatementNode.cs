using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class PrintStatementNode(ExpressionNode expression, SourceSpan span, bool appendNewLine = false)
    : StatementNode(span)
{
    public ExpressionNode Expression { get; } = expression;
    public bool AppendNewLine { get; } = appendNewLine;
}
