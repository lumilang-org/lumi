using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

// e.g. where T : Vec2
public sealed class TypeConstraintNode(
    string parameter,
    string boundType,
    SourceSpan span) : ASTNode(span)
{
    public string Parameter { get; } = parameter;
    public string BoundType { get; } = boundType;
}
