using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Ast.Declarations;

public sealed class FunctionDeclarationNode(
    string name,
    IReadOnlyList<FunctionParameterNode> parameters,
    string returnType,
    List<StatementNode> body,
    SourceSpan span,
    VisibilityModifier visibilityModifier = VisibilityModifier.Private) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<FunctionParameterNode> Parameters { get; } = parameters;
    public string ReturnType { get; } = returnType;
    public List<StatementNode> Body { get; } = body;
    public VisibilityModifier VisibilityModifier { get; } = visibilityModifier;
}
