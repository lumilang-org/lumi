using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

// runs when the enclosing block is left (LIFO order).
public sealed class DeferStatementNode(
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public IReadOnlyList<StatementNode> Body { get; } = body;
}
