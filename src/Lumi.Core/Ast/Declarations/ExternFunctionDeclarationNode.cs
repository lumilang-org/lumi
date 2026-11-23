using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class ExternFunctionDeclarationNode(
    string name,
    IReadOnlyList<FunctionParameterNode> parameters,
    string returnType,
    SourceSpan span) : ASTNode(span)
{
    public string Name { get; } = name;
    public IReadOnlyList<FunctionParameterNode> Parameters { get; } = parameters;
    public string ReturnType { get; } = returnType;
}
