using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Declarations;

public sealed class ProgramNode(
    ModuleDeclarationNode? module,
    IReadOnlyList<ImportDeclarationNode> imports,
    IReadOnlyList<FunctionDeclarationNode> functions,
    IReadOnlyList<StructDeclarationNode> structs,
    IReadOnlyList<EnumDeclarationNode> enums,
    IReadOnlyList<ExternFunctionDeclarationNode> externFunctions,
    SourceSpan span,
    string fileName) : ASTNode(span)
{
    public string FileName { get; } = fileName;
    public ModuleDeclarationNode? Module { get; } = module;
    public IReadOnlyList<ImportDeclarationNode> Imports { get; } = imports;
    private readonly List<FunctionDeclarationNode> _functions = [..functions];
    private readonly List<StructDeclarationNode> _structs = [..structs];

    public IReadOnlyList<FunctionDeclarationNode> Functions => _functions;
    public IReadOnlyList<StructDeclarationNode> Structs => _structs;

    // Generic templates are kept apart: only their specializations are checked and emitted.
    public List<FunctionDeclarationNode> GenericFunctions { get; } = [];
    public List<StructDeclarationNode> GenericStructs { get; } = [];

    public void AddSpecialization(FunctionDeclarationNode function) => _functions.Add(function);
    public void AddSpecialization(StructDeclarationNode structDecl) => _structs.Add(structDecl);
    private readonly List<EnumDeclarationNode> _enums = [..enums];
    public IReadOnlyList<EnumDeclarationNode> Enums => _enums;
    public List<EnumDeclarationNode> GenericEnums { get; } = [];
    public void AddSpecialization(EnumDeclarationNode enumDecl) => _enums.Add(enumDecl);
    public IReadOnlyList<ExternFunctionDeclarationNode> ExternFunctions { get; } = externFunctions;

    // Module-level functions followed by the functions nested in structs.
    public IEnumerable<FunctionDeclarationNode> AllFunctions =>
        Functions.Concat(Structs.SelectMany(structDecl => structDecl.Methods));
}
