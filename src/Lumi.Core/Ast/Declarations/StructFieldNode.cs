using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class StructFieldNode(string type, string name, SourceSpan span) : ASTNode(span)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}
