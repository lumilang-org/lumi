using Lumi.Core.Ast;

namespace Lumi.Core.Semantics;

public sealed class SemanticModel
{
    private readonly Dictionary<ASTNode, string> _mangledNames = new();
    private readonly Dictionary<ASTNode, string> _nodeTypes = new();
    public FunctionRegistry Functions { get; } = new();

    public void SetType(ASTNode node, string type)
    {
        _nodeTypes[node] = type;
    }

    public string GetType(ASTNode node)
    {
        if (_nodeTypes.TryGetValue(node, out var type))
        {
            return type;
        }

        throw new Exception(
            $"Semantic Error: No resolved type exists for node '{node.GetType().Name}'.");
    }

    public void SetMangledName(ASTNode node, string mangledName)
    {
        _mangledNames[node] = mangledName;
    }

    public string GetMangledName(ASTNode node)
    {
        if (_mangledNames.TryGetValue(node, out var name))
        {
            return name;
        }

        if (node is FunctionDeclarationNode func)
        {
            return func.Name;
        }

        if (node is StructDeclarationNode @struct)
        {
            return @struct.Name;
        }

        if (node is FunctionCallExpressionNode call)
        {
            return call.Name;
        }

        return string.Empty;
    }

    public void SetExpressionType(ExpressionNode expression, string type)
    {
        SetType(expression, type);
    }

    public string GetExpressionType(ExpressionNode expression)
    {
        return GetType(expression);
    }
}
