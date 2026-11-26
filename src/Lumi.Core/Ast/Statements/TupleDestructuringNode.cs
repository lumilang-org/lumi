using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Statements;

// A destructuring pattern: a leaf binds one element (Name; "_" discards it),
// a branch destructures a nested tuple.
public sealed class TuplePatternNode(
    string? name,
    IReadOnlyList<TuplePatternNode>? elements,
    SourceSpan span) : ASTNode(span)
{
    public string? Name { get; } = name;
    public IReadOnlyList<TuplePatternNode>? Elements { get; } = elements;
    public bool IsLeaf => Elements is null;
}

// let (a, (b, _)) = expr
public sealed class TupleDestructuringNode(
    TuplePatternNode pattern,
    ExpressionNode initializer,
    SourceSpan span) : StatementNode(span)
{
    public TuplePatternNode Pattern { get; } = pattern;
    public ExpressionNode Initializer { get; } = initializer;
}
