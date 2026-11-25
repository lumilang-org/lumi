using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class ForInStatementNode(
    string variableName,
    ExpressionNode iterable,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public string VariableName { get; } = variableName;
    public ExpressionNode Iterable { get; } = iterable;
    public IReadOnlyList<StatementNode> Body { get; } = body;
}
