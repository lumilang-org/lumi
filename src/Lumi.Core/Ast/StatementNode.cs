using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast;

public abstract class StatementNode(SourceSpan span)
    : ASTNode(span);
