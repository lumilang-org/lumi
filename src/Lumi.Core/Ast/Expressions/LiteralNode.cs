using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public abstract class LiteralNode(SourceSpan span)
    : ExpressionNode(span);

public sealed class LiteralNode<T>(
    T value,
    SourceSpan span)
    : LiteralNode(span)
{
    public T Value { get; } = value;
}
