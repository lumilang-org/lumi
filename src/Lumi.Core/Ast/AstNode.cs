using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast;

// Make the source span optional to avoid breaking the entire AST
// Todo: Remove optional from source span
public abstract class ASTNode(SourceSpan span)
{
    public SourceSpan Span { get; } = span;
}

public abstract class ExpressionNode(SourceSpan span)
    : ASTNode(span);

public abstract class StatementNode(SourceSpan span)
    : ASTNode(span);

public sealed class BreakStatementNode(SourceSpan span)
    : StatementNode(span);

public sealed class ContinueStatementNode(SourceSpan span)
    : StatementNode(span);

public sealed class UnaryExpressionNode(
    string @operator,
    ExpressionNode operand,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Operator { get; } = @operator;
    public ExpressionNode Operand { get; } = operand;
}

public sealed class IdentifierNode(
    string name,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Name { get; } = name;
}

public sealed class BinaryExpressionNode(
    ExpressionNode left,
    string op,
    ExpressionNode right,
    SourceSpan span)
    : ExpressionNode(span)
{
    public ExpressionNode Left { get; } = left;
    public string Operator { get; } = op;
    public ExpressionNode Right { get; } = right;
}

public sealed class PrintStatementNode(ExpressionNode expression, SourceSpan span, bool appendNewLine = false)
    : StatementNode(span)
{
    public ExpressionNode Expression { get; } = expression;
    public bool AppendNewLine { get; } = appendNewLine;
}

public sealed class VariableDeclarationNode(
    string type,
    string name,
    ExpressionNode? initializer,
    SourceSpan span) : StatementNode(span)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
    public ExpressionNode? Initializer { get; } = initializer;
}

public sealed class FunctionDeclarationNode(
    string name,
    IReadOnlyList<FunctionParameterNode> parameters,
    string returnType,
    List<StatementNode> body,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<FunctionParameterNode> Parameters { get; } = parameters;
    public string ReturnType { get; } = returnType;
    public List<StatementNode> Body { get; } = body;
}

public sealed class ExpressionStatementNode(ExpressionNode expression, SourceSpan span) : StatementNode(span)
{
    public ExpressionNode Expression { get; } = expression;
}

public sealed class StructFieldNode(string type, string name, SourceSpan span) : ASTNode(span)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}

public sealed class StructDeclarationNode(
    string name,
    IReadOnlyList<StructFieldNode> fields,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<StructFieldNode> Fields { get; } = fields;
}

public sealed class StructFieldInitNode(
    string fieldName,
    ExpressionNode value,
    SourceSpan span) : ASTNode(span)
{
    public string FieldName { get; } = fieldName;
    public ExpressionNode Value { get; } = value;
}

public sealed class StructLiteralNode(
    string structName,
    IReadOnlyList<StructFieldInitNode> initializers,
    SourceSpan span) : ExpressionNode(span)
{
    public string StructName { get; } = structName;
    public IReadOnlyList<StructFieldInitNode> Initializers { get; } = initializers;
}

public sealed class MemberAccessNode(
    ExpressionNode target,
    string memberName,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public string MemberName { get; } = memberName;
}

// Generalize assignment target so both `a = 1` and `p.x = 1` work
public sealed class TargetAssignmentStatementNode(
    ExpressionNode target,
    ExpressionNode value,
    SourceSpan span) : StatementNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode Value { get; } = value;
}

public sealed class ReturnStatementNode(ExpressionNode? expression, SourceSpan span) : StatementNode(span)
{
    public ExpressionNode? Expression { get; } = expression;
}

public abstract class LiteralNode(SourceSpan span)
    : ExpressionNode(span);

public sealed class LiteralNode<T>(
    T value,
    SourceSpan span)
    : LiteralNode(span)
{
    public T Value { get; } = value;
}

public sealed class IfStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> thenBody,
    SourceSpan span,
    IReadOnlyList<StatementNode>? elseBody = null)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> ThenBody { get; } = thenBody;

    public IReadOnlyList<StatementNode>? ElseBody { get; } = elseBody;
}

public sealed class AssignmentStatementNode(
    string name,
    ExpressionNode value,
    SourceSpan span) : StatementNode(span)
{
    public string Name { get; } = name;

    public ExpressionNode Value { get; } = value;
}

public sealed class WhileStatementNode(
    ExpressionNode condition,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public ExpressionNode Condition { get; } = condition;

    public IReadOnlyList<StatementNode> Body { get; } = body;
}

public sealed class ForStatementNode(
    StatementNode? initializer,
    ExpressionNode? condition,
    StatementNode? update,
    IReadOnlyList<StatementNode> body,
    SourceSpan span)
    : StatementNode(span)
{
    public StatementNode? Initializer { get; } = initializer;
    public ExpressionNode? Condition { get; } = condition;
    public StatementNode? Update { get; } = update;
    public IReadOnlyList<StatementNode> Body { get; } = body;
}

public sealed class FunctionParameterNode(
    string type,
    string name,
    SourceSpan span) : ASTNode(span)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}

public sealed class ProgramNode(
    ModuleDeclarationNode? module,
    IReadOnlyList<ImportDeclarationNode> imports,
    IReadOnlyList<FunctionDeclarationNode> functions,
    IReadOnlyList<StructDeclarationNode> structs,
    SourceSpan span) : ASTNode(span)
{
    public ModuleDeclarationNode? Module { get; } = module;
    public IReadOnlyList<ImportDeclarationNode> Imports { get; } = imports;
    public IReadOnlyList<FunctionDeclarationNode> Functions { get; } = functions;
    public IReadOnlyList<StructDeclarationNode> Structs { get; } = structs;
}

public sealed class ArrayLiteralNode(
    IReadOnlyList<ExpressionNode> elements,
    SourceSpan span) : ExpressionNode(span)
{
    public IReadOnlyList<ExpressionNode> Elements { get; } = elements;
}

public sealed class IndexExpressionNode(
    ExpressionNode target,
    ExpressionNode index,
    SourceSpan span) : ExpressionNode(span)
{
    public ExpressionNode Target { get; } = target;
    public ExpressionNode Index { get; } = index;
}

public sealed class FunctionCallExpressionNode(
    string name,
    IReadOnlyList<ExpressionNode> arguments,
    SourceSpan span)
    : ExpressionNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<ExpressionNode> Arguments { get; } = arguments;
}

public sealed class ModuleDeclarationNode(
    IReadOnlyList<string> pathSegments,
    SourceSpan span)
    : ASTNode(span)
{
    public IReadOnlyList<string> PathSegments { get; } = pathSegments;
}

public sealed class ImportDeclarationNode(
    IReadOnlyList<string> pathSegments,
    SourceSpan span)
    : ASTNode(span)
{
    public IReadOnlyList<string> PathSegments { get; } = pathSegments;
}
