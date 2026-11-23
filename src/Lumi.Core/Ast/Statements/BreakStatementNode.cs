using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class BreakStatementNode(SourceSpan span)
    : StatementNode(span);
