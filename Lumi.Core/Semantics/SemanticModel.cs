using Lumi.Core.Ast;

namespace Lumi.Core.Semantics;

public sealed class SemanticModel
{
    private readonly Dictionary<ExpressionNode, string> _expressionTypes = new();

    public void SetExpressionType(ExpressionNode expression, string type)
    {
        _expressionTypes[expression] = type;
    }

    public string GetExpressionType(ExpressionNode expression)
    {
        if (_expressionTypes.TryGetValue(expression, out var type))
        {
            return type;
        }

        throw new Exception(
            $"Semantic Error: No resolved type exists for expression '{expression.GetType().Name}'.");
    }
}
