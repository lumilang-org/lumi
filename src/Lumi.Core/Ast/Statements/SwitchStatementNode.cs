using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

// One arm of a switch: `pattern, pattern => body`.
public sealed class SwitchArmNode(
    IReadOnlyList<ExpressionNode> patterns,
    IReadOnlyList<StatementNode> body,
    SourceSpan span) : ASTNode(span)
{
    public IReadOnlyList<ExpressionNode> Patterns { get; } = patterns;
    public IReadOnlyList<StatementNode> Body { get; } = body;
}

public sealed class SwitchStatementNode(
    ExpressionNode subject,
    IReadOnlyList<SwitchArmNode> arms,
    IReadOnlyList<StatementNode>? defaultBody,
    SourceSpan span) : StatementNode(span)
{
    public ExpressionNode Subject { get; } = subject;
    public IReadOnlyList<SwitchArmNode> Arms { get; } = arms;

    // Null when there is no `default` arm.
    public IReadOnlyList<StatementNode>? DefaultBody { get; } = defaultBody;
}
