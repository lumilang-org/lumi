using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

public sealed class StructFieldInitNode(
    string fieldName,
    ExpressionNode value,
    SourceSpan span) : ASTNode(span)
{
    public string FieldName { get; } = fieldName;
    public ExpressionNode Value { get; } = value;
}
