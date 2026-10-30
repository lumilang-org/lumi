using Lumi.Core.Ast;

namespace Lumi.Core.Semantics;

public sealed class ReturnAnalyzer
{
    public bool AlwaysReturns(IReadOnlyList<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            if (AlwaysReturns(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool AlwaysReturns(StatementNode statement)
    {
        return statement switch
        {
            ReturnStatementNode => true,

            IfStatementNode ifStatement =>
                ifStatement.ElseBody is not null &&
                AlwaysReturns(ifStatement.ThenBody) &&
                AlwaysReturns(ifStatement.ElseBody),

            _ => false
        };
    }
}
