using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

/// <summary>
///     A prefix (<c>++x</c>) or postfix (<c>x++</c>) increment/decrement. A prefix update
///     yields the new value, a postfix update yields the previous one.
/// </summary>
public sealed class UpdateExpressionNode(
    string @operator,
    ExpressionNode target,
    bool isPrefix,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Operator { get; } = @operator;
    public ExpressionNode Target { get; } = target;
    public bool IsPrefix { get; } = isPrefix;
    public bool IsPostfix => !IsPrefix;
}
