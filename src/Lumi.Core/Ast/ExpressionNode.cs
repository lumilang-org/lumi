using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast;

public abstract class ExpressionNode(SourceSpan span)
    : ASTNode(span);
