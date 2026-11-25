using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Ast.Declarations;

public sealed class StructFieldNode(
    string type,
    string name,
    SourceSpan span,
    VisibilityModifier visibilityModifier = VisibilityModifier.Private) : ASTNode(span)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
    public VisibilityModifier VisibilityModifier { get; } = visibilityModifier;
}
