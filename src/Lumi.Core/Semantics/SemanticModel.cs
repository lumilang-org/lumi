using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;

namespace Lumi.Core.Semantics;

public sealed class SemanticModel
{
    private readonly Dictionary<ASTNode, string> _mangledNames = new();
    private readonly Dictionary<ASTNode, string> _nodeTypes = new();
    private readonly HashSet<ASTNode> _builtinCalls = new();
    private readonly Dictionary<ASTNode, CallReceiver> _receivers = new();
    private readonly HashSet<ASTNode> _enumConstructions = new();
    private readonly HashSet<ASTNode> _exhaustiveSwitches = new();
    private readonly HashSet<ASTNode> _sliceGets = new();
    public FunctionRegistry Functions { get; } = new();

    // Marks `slice.get(i)` / `array.get(i)`; its mangled name is the Option enum it returns.
    public void MarkSliceGet(ASTNode node) => _sliceGets.Add(node);

    public bool IsSliceGet(ASTNode node) => _sliceGets.Contains(node);

    public void MarkExhaustiveSwitch(ASTNode node) => _exhaustiveSwitches.Add(node);

    public bool IsExhaustiveSwitch(ASTNode node) => _exhaustiveSwitches.Contains(node);

    // Marks a MemberAccess / MethodCall / EnumStructLiteral that builds an enum variant;
    // its mangled name is the enum's.
    public void MarkEnumConstruction(ASTNode node)
    {
        _enumConstructions.Add(node);
    }

    public bool IsEnumConstruction(ASTNode node)
    {
        return _enumConstructions.Contains(node);
    }

    public void MarkBuiltinCall(FunctionCallExpressionNode call)
    {
        _builtinCalls.Add(call);
    }

    public bool IsBuiltinCall(FunctionCallExpressionNode call)
    {
        return _builtinCalls.Contains(call);
    }

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

    public void SetReceiver(MethodCallExpressionNode call, CallReceiver receiver)
    {
        _receivers[call] = receiver;
    }

    public CallReceiver GetReceiver(MethodCallExpressionNode call)
    {
        return _receivers[call];
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
