using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

// target.method(args): an instance method call, or Struct.function(args) for associated functions.
public sealed class MethodCallExpressionNode(
    ExpressionNode target,
    string methodName,
    IReadOnlyList<ExpressionNode> arguments,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public string MethodName { get; } = methodName;
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;
}
