namespace Lumi.Core.Ast;

public abstract class ASTNode
{
}

// Base class for expressions that evaluate to a value (e.g., 10, a + b)
public abstract class ExpressionNode : ASTNode
{
}

// Base class for statements that perform actions (e.g., variable declarations)
public abstract class StatementNode : ASTNode
{
}

public sealed class UnaryExpressionNode(
    string @operator,
    ExpressionNode operand) : ExpressionNode
{
    public string Operator { get; } = @operator;
    public ExpressionNode Operand { get; } = operand;
}

public sealed class IdentifierNode(string name) : ExpressionNode
{
    public string Name { get; } = name;
}

public sealed class BinaryExpressionNode(ExpressionNode left, string op, ExpressionNode right) : ExpressionNode
{
    public ExpressionNode Left { get; } = left;
    public string Operator { get; } = op;
    public ExpressionNode Right { get; } = right;
}

public sealed class PrintStatementNode(ExpressionNode expression, bool appendNewLine = false) : StatementNode
{
    public ExpressionNode Expression { get; } = expression;
    public bool AppendNewLine { get; } = appendNewLine;
}

public sealed class VariableDeclarationNode(string type, string name, ExpressionNode initializer) : StatementNode
{
    public string Type { get; } = type;
    public string Name { get; } = name;
    public ExpressionNode Initializer { get; } = initializer;
}

public sealed class FunctionDeclarationNode(string name, string returnType, List<StatementNode> body) : ASTNode
{
    public string Name { get; } = name;
    public string ReturnType { get; } = returnType;
    public List<StatementNode> Body { get; } = body;
}

public sealed class ExpressionStatementNode(ExpressionNode expression) : StatementNode
{
    public ExpressionNode Expression { get; } = expression;
}

public sealed class ReturnStatementNode(ExpressionNode value) : StatementNode
{
    public ExpressionNode Value { get; } = value;
}

public abstract class LiteralNode : ExpressionNode;

public sealed class LiteralNode<T>(T value) : LiteralNode
{
    public T Value { get; } = value;
}

public sealed class IfStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> thenBody,
    IReadOnlyList<StatementNode>? elseBody = null)
    : StatementNode
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> ThenBody { get; } = thenBody;

    public IReadOnlyList<StatementNode>? ElseBody { get; } = elseBody;
}