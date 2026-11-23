using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class ImportDeclarationNode(
    IReadOnlyList<string> pathSegments,
    SourceSpan span)
    : ASTNode(span)
{
    public IReadOnlyList<string> PathSegments { get; } = pathSegments;
}
