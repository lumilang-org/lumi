using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class IdentifierNode(
    string name,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Name { get; } = name;
}
