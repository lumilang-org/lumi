using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

/// <summary>A double-quoted string literal. <see cref="Value" /> is the unescaped text.</summary>
public sealed class StringLiteralNode(
    string value,
    SourceSpan span) : ExpressionNode(span)
{
    public string Value { get; } = value;
}
