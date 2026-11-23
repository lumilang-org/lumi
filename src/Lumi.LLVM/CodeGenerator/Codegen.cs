using LLVMSharp.Interop;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Helpers;
using Lumi.LLVM.Structs;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    private readonly LLVMBuilderRef _builder;
    private readonly LLVMContextRef _context;
    private readonly Dictionary<string, (LLVMValueRef Function, LLVMTypeRef FunctionType)> _functions = new();
    private readonly LlvmTypeHelper _llvmTypeHelper;
    private readonly Stack<LoopTargets> _loopTargets = new();
    private readonly LLVMModuleRef _module;
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<string, StructLayoutInfo> _structLayouts = new();

    // Identical string literals share one private constant global per module.
    private readonly Dictionary<string, LLVMValueRef> _stringLiterals = new();

    // Track the return type of the function currently being generated
    private LLVMTypeRef _currentReturnType;

    private string _currentReturnTypeName = TypeConst.Void;

    private VariableScope _currentScope;

    public Codegen(string moduleName, SemanticModel semanticModel)
    {
        _context = LLVMContextRef.Create();
        _module = _context.CreateModuleWithName(moduleName);
        _builder = _context.CreateBuilder();
        _semanticModel = semanticModel;
        _llvmTypeHelper = new LlvmTypeHelper(_context, _builder);
        _currentScope = new VariableScope();

        LLVMSharp.Interop.LLVM.InitializeNativeTarget();
        LLVMSharp.Interop.LLVM.InitializeNativeAsmPrinter();
        LLVMSharp.Interop.LLVM.InitializeNativeAsmParser();
    }

    public void GenerateProgram(ProgramNode program)
    {
        GenerateCompilationUnit([program]);
    }

    public void GenerateCompilationUnit(IReadOnlyList<ProgramNode> modules)
    {
        foreach (var module in modules)
        {
            RegisterStructs(module);
        }

        // Declare every function in the LLVM module.
        foreach (var module in modules)
        {
            foreach (var funcNode in module.Functions)
            {
                var mangledName = _semanticModel.GetMangledName(funcNode);
                var resolvedReturnType = _semanticModel.GetType(funcNode);
                var returnType = ResolveType(resolvedReturnType);

                var parameterTypes = funcNode.Parameters
                    .Select(parameter => ResolveType(_semanticModel.GetType(parameter)))
                    .ToArray();

                var functionType = LLVMTypeRef.CreateFunction(
                    returnType,
                    parameterTypes);

                var function = _module.AddFunction(
                    mangledName,
                    functionType);

                _functions.Add(
                    mangledName,
                    (function, functionType));
            }
        }

        foreach (var module in modules)
        {
            foreach (var funcNode in module.Functions)
            {
                var mangledName = _semanticModel.GetMangledName(funcNode);
                var function = _functions[mangledName].Function;
                GenerateFunctionBody(funcNode, function);
            }
        }

        if (!_module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var message))
        {
            throw new InvalidOperationException($"LLVM module verification failed:\n{message}");
        }
    }

    private void GenerateFunctionBody(
        FunctionDeclarationNode funcNode,
        LLVMValueRef function)
    {
        var resolvedReturnType = _semanticModel.GetType(funcNode);
        _currentReturnType = ResolveType(resolvedReturnType);
        _currentReturnTypeName = resolvedReturnType;

        var entryBlock = _context.AppendBasicBlock(function, "entry");
        _builder.PositionAtEnd(entryBlock);

        _currentScope.Clear();

        // Register function parameters in the root scope
        for (var i = 0; i < funcNode.Parameters.Count; i++)
        {
            var parameter = funcNode.Parameters[i];
            var resolvedType = _semanticModel.GetType(parameter);
            var parameterType = ResolveType(resolvedType);

            var parameterValue = function.GetParam((uint)i);

            var alloca = _builder.BuildAlloca(
                parameterType,
                parameter.Name);

            _builder.BuildStore(parameterValue, alloca);

            _currentScope.Declare(
                parameter.Name,
                new VariableSymbol(
                    resolvedType,
                    parameterType,
                    alloca));
        }

        GenerateScopedStatements(funcNode.Body);

        var currentBlock = _builder.InsertBlock;

        if (!HasTerminator(currentBlock))
        {
            if (funcNode.ReturnType == TypeConst.Void)
            {
                _builder.BuildRetVoid();
            }
            else
            {
                _builder.BuildRet(
                    LLVMValueRef.CreateConstInt(_currentReturnType, 0));
            }
        }
    }

    public string GetModuleIr()
    {
        return _module.PrintToString();
    }

    public void DumpModuleToConsole()
    {
        _module.Dump();
    }

    public void CompileToNativeObject(string outputFileName)
    {
        var targetTriple = LLVMTargetRef.DefaultTriple;
        _module.Target = targetTriple;

        var target = LLVMTargetRef.GetTargetFromTriple(targetTriple);

        var targetMachine = target.CreateTargetMachine(
            targetTriple,
            "generic",
            "",
            LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            LLVMRelocMode.LLVMRelocDefault,
            LLVMCodeModel.LLVMCodeModelDefault
        );

        // Retrieve the proper Data Layout string representation from the target machine
        var dataLayout = targetMachine.CreateTargetDataLayout();
        _module.DataLayout = dataLayout.StringRepresentation;

        // Emit the compilation tree into a native binary object file (.o)
        targetMachine.EmitToFile(
            _module,
            outputFileName,
            LLVMCodeGenFileType.LLVMObjectFile
        );

        Console.WriteLine($"[Codegen]: Object file written successfully to: {outputFileName}");
    }

    private sealed record LoopTargets(
        LLVMBasicBlockRef BreakTarget,
        LLVMBasicBlockRef ContinueTarget);
}
