using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast;

// Make the source span optional to avoid breaking the entire AST
public abstract class ASTNode(SourceSpan span)
{
    public SourceSpan Span { get; } = span;
}
