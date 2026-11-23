using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class MemberAccessNode(
    ExpressionNode target,
    string memberName,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public string MemberName { get; } = memberName;
}
