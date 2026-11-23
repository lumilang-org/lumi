using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class FunctionCallExpressionNode(
    string name,
    IReadOnlyList<ExpressionNode> arguments,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;
}
