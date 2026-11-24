using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class SizeOfExpressionNode(string targetType, SourceSpan span) : ExpressionNode(span)
{
    public string TargetType { get; } = targetType;
}
