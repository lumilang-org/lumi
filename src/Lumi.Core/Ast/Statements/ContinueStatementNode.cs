using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

public sealed class ContinueStatementNode(SourceSpan span)
    : StatementNode(span);
