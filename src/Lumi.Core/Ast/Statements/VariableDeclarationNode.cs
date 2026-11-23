using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

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
