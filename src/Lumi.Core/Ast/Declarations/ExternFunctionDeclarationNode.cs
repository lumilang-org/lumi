using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Ast.Declarations;

public sealed class ExternFunctionDeclarationNode(
    string name,
    IReadOnlyList<FunctionParameterNode> parameters,
    string returnType,
    SourceSpan span,
    VisibilityModifier visibilityModifier = VisibilityModifier.Private) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<FunctionParameterNode> Parameters { get; } = parameters;
    public string ReturnType { get; } = returnType;
    public VisibilityModifier VisibilityModifier { get; } = visibilityModifier;
}
