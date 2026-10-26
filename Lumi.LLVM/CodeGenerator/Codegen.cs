namespace Lumi.LLVM.CodeGenerator;

using LLVMSharp.Interop;
using Core.Ast;

public sealed class Codegen
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMBuilderRef _builder;
    
    // Map tracking variable references to their stack allocations (Alloca)
    private readonly Dictionary<string, LLVMValueRef> _namedValues = new();

    // Track the return type of the function currently being generated
    private LLVMTypeRef _currentReturnType;

    public Codegen(string moduleName)
    {
        _context = LLVMContextRef.Create();
        _module = _context.CreateModuleWithName(moduleName);
        _builder = _context.CreateBuilder();
        
        LLVM.InitializeNativeTarget();
        LLVM.InitializeNativeAsmPrinter();
        LLVM.InitializeNativeAsmParser();
    }

    public void GenerateFunction(FunctionDeclarationNode funcNode)
    {
        _currentReturnType = ResolveType(funcNode.ReturnType);
        var functionType = LLVMTypeRef.CreateFunction(_currentReturnType, []);
        var function = _module.AddFunction(funcNode.Name, functionType);

        var entryBlock = _context.AppendBasicBlock(function, "entry");
        _builder.PositionAtEnd(entryBlock);

        // Clear local scope variables for the new function
        _namedValues.Clear();

        // Track if a return statement was generated
        bool hasReturned = false;

        foreach (var statement in funcNode.Body)
        {
            GenerateStatement(statement);
        
            if (statement is ReturnStatementNode)
            {
                hasReturned = true;
                break; 
            }
        }

        // Only emit fallback terminator if the function body didn't explicitly return
        if (!hasReturned)
        {
            if (funcNode.ReturnType == "void")
            {
                _builder.BuildRetVoid();
            }
            else
            {
                var defaultZero = LLVMValueRef.CreateConstInt(_currentReturnType, 0);
                _builder.BuildRet(defaultZero);
            }
        }

        _module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case VariableDeclarationNode varDecl:
                var value = GenerateExpression(varDecl.Initializer);
                var type = ResolveType(varDecl.Type);
                var alloca = _builder.BuildAlloca(type, varDecl.Name);
                
                _builder.BuildStore(value, alloca);
                _namedValues[varDecl.Name] = alloca;
                break;

            case ExpressionStatementNode exprStmt:
                GenerateExpression(exprStmt.Expression);
                break;

            case ReturnStatementNode retStmt:
                var returnValue = GenerateExpression(retStmt.Value);
                
                // Dynamically cast/extend/truncate to match the function's actual declared return type
                if (returnValue.TypeOf != _currentReturnType)
                {
                    if (_currentReturnType.IntWidth > returnValue.TypeOf.IntWidth)
                    {
                        returnValue = _builder.BuildSExt(returnValue, _currentReturnType, "extended_ret");
                    }
                    else if (_currentReturnType.IntWidth < returnValue.TypeOf.IntWidth)
                    {
                        returnValue = _builder.BuildTrunc(returnValue, _currentReturnType, "truncated_ret");
                    }
                }

                _builder.BuildRet(returnValue);
                break;
            case PrintStatementNode printStmt:
                var printValue = GenerateExpression(printStmt.Expression);
                WriterUtils.EmitPrintInt(printValue, _module, _context, _builder);
                break;
        }
    }

    private LLVMValueRef GenerateExpression(ExpressionNode expression)
    {
        return expression switch
        {
            NumberLiteralNode num => 
                LLVMValueRef.CreateConstInt(_context.Int16Type, ulong.Parse(num.Value)),

            IdentifierNode id => 
                LoadVariable(id.Name),

            BinaryExpressionNode binary => 
                GenerateBinary(binary),

            _ => throw new Exception("Unknown IR generation node target.")
        };
    }

    private LLVMValueRef LoadVariable(string name)
    {
        if (_namedValues.TryGetValue(name, out var alloca))
        {
            return _builder.BuildLoad2(_context.Int16Type, alloca, name);
        }
        throw new Exception($"Compiler Error: Undefined identifier '{name}' reached backend.");
    }

    private LLVMValueRef GenerateBinary(BinaryExpressionNode binary)
    {
        var left = GenerateExpression(binary.Left);
        var right = GenerateExpression(binary.Right);

        return binary.Operator switch
        {
            "+" => _builder.BuildAdd(left, right, "addtmp"),
            "-" => _builder.BuildSub(left, right, "subtmp"),
            _ => throw new Exception($"Unsupported IR binary operator: {binary.Operator}")
        };
    }

    private LLVMTypeRef ResolveType(string typeName) => typeName switch
    {
        "i16" => _context.Int16Type,
        "i32" => _context.Int32Type,
        "i64" => _context.Int64Type,
        "void" => _context.VoidType,
        _ => throw new Exception($"Unsupported backend data primitive type: {typeName}")
    };

    public void DumpModuleToConsole()
    {
        _module.Dump();
    }
    
    public void CompileToNativeObject(string outputFileName)
    {
        // Fetch the default target triple for your machine
        string targetTriple = LLVMTargetRef.DefaultTriple;
        _module.Target = targetTriple;

        // 2. Locate the specific target compilation engine
        var target = LLVMTargetRef.GetTargetFromTriple(targetTriple);

        // 3. Create the Target Machine profile configuration
        var targetMachine = target.CreateTargetMachine(
            triple: targetTriple,
            cpu: "generic",
            features: "",
            level: LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            reloc: LLVMRelocMode.LLVMRelocDefault,
            codeModel: LLVMCodeModel.LLVMCodeModelDefault
        );

        // Retrieve the proper Data Layout string representation from the target machine
        var dataLayout = targetMachine.CreateTargetDataLayout();
        _module.DataLayout = dataLayout.StringRepresentation;

        // 4. Emit the compilation tree into a native binary object file (.o)
        targetMachine.EmitToFile(
            _module, 
            outputFileName, 
            LLVMCodeGenFileType.LLVMObjectFile
        );
        
        Console.WriteLine($"[Codegen]: Object file written successfully to: {outputFileName}");
    }
}