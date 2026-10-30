namespace Lumi.Core.Ast;

public abstract class ASTNode
{
}

// Base class for expressions that evaluate to a value (e.g., 10, a + b)
public abstract class ExpressionNode : ASTNode
{
}

// Base class for statements that perform actions (e.g., variable declarations)
public abstract class StatementNode : ASTNode;

public sealed class ContinueStatementNode : StatementNode;

public sealed class BreakStatementNode : StatementNode;

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

public sealed class FunctionDeclarationNode(
    string name,
    IReadOnlyList<FunctionParameterNode> parameters,
    string returnType,
    List<StatementNode> body) : ASTNode
{
    public string Name { get; } = name;
    public IReadOnlyList<FunctionParameterNode> Parameters { get; } = parameters;
    public string ReturnType { get; } = returnType;
    public List<StatementNode> Body { get; } = body;
}

public sealed class ExpressionStatementNode(ExpressionNode expression) : StatementNode
{
    public ExpressionNode Expression { get; } = expression;
}

public sealed class ReturnStatementNode(ExpressionNode? expression) : StatementNode
{
    public ExpressionNode? Expression { get; } = expression;
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

public sealed class AssignmentStatementNode(
    string name,
    ExpressionNode value) : StatementNode
{
    public string Name { get; } = name;

    public ExpressionNode Value { get; } = value;
}

public sealed class WhileStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> body)
    : StatementNode
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> Body { get; } = body;
}

public sealed class ForStatementNode(
    StatementNode? initializer,
    ExpressionNode? condition,
    StatementNode? update,
    IReadOnlyList<StatementNode> body)
    : StatementNode
{
    public StatementNode? Initializer { get; } = initializer;
    public ExpressionNode? Condition { get; } = condition;
    public StatementNode? Update { get; } = update;
    public IReadOnlyList<StatementNode> Body { get; } = body;
}

public sealed class FunctionParameterNode(
    string type,
    string name) : ASTNode
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}

public sealed class ProgramNode(
    IReadOnlyList<FunctionDeclarationNode> functions) : ASTNode
{
    public IReadOnlyList<FunctionDeclarationNode> Functions { get; } = functions;
}

public sealed class FunctionCallExpressionNode(
    string name,
    IReadOnlyList<ExpressionNode> arguments) : ExpressionNode
{
    public string Name { get; } = name;
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;
}
