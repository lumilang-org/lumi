using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class ProgramNode(
    ModuleDeclarationNode? module,
    IReadOnlyList<ImportDeclarationNode> imports,
    IReadOnlyList<FunctionDeclarationNode> functions,
    IReadOnlyList<StructDeclarationNode> structs,
    SourceSpan span,
    string fileName) : ASTNode(span)
{
    public string FileName { get; } = fileName;
    public ModuleDeclarationNode? Module { get; } = module;
    public IReadOnlyList<ImportDeclarationNode> Imports { get; } = imports;
    public IReadOnlyList<FunctionDeclarationNode> Functions { get; } = functions;
    public IReadOnlyList<StructDeclarationNode> Structs { get; } = structs;
}
