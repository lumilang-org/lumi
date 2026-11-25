using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

// Enum.Variant { field: value, ... }. In a switch pattern the values are binding identifiers.
public sealed class EnumStructLiteralNode(
    string enumName,
    string variantName,
    IReadOnlyList<StructFieldInitNode> initializers,
    SourceSpan span) : ExpressionNode(span)
{
    public string EnumName { get; } = enumName;
    public string VariantName { get; } = variantName;
    public IReadOnlyList<StructFieldInitNode> Initializers { get; } = initializers;
}
