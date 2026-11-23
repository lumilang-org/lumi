using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Helpers;
using Lumi.LLVM.Structs;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.CodeGenerator;

public sealed class Codegen
{
    private readonly LLVMBuilderRef _builder;
    private readonly LLVMContextRef _context;
    private readonly Dictionary<string, (LLVMValueRef Function, LLVMTypeRef FunctionType)> _functions = new();
    private readonly LlvmTypeHelper _llvmTypeHelper;

    private readonly Stack<LoopTargets> _loopTargets = new();
    private readonly LLVMModuleRef _module;
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<string, StructLayoutInfo> _structLayouts = new();

    // Track the return type of the function currently being generated
    private LLVMTypeRef _currentReturnType;
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

        _module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);
    }

    private void GenerateFunctionBody(
        FunctionDeclarationNode funcNode,
        LLVMValueRef function)
    {
        var resolvedReturnType = _semanticModel.GetType(funcNode);
        _currentReturnType = ResolveType(resolvedReturnType);

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

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case VariableDeclarationNode varDecl:
            {
                var resolvedType = _semanticModel.GetType(varDecl);
                var declaredType = ResolveType(resolvedType);

                if (varDecl.Initializer == null)
                {
                    var alloca = _builder.BuildAlloca(declaredType, varDecl.Name);
                    _currentScope.Declare(varDecl.Name, new VariableSymbol(resolvedType, declaredType, alloca));
                    break;
                }

                if (_structLayouts.TryGetValue(resolvedType, out var structLayout) &&
                    varDecl.Initializer is StructLiteralNode structLiteral)
                {
                    var alloca = _builder.BuildAlloca(structLayout.StructType, varDecl.Name);
                    EmitStructLiteralFields(structLiteral, alloca, structLayout);
                    _currentScope.Declare(
                        varDecl.Name,
                        new VariableSymbol(resolvedType, structLayout.StructType, alloca));
                    break;
                }

                if (resolvedType.Contains('[') && varDecl.Initializer is ArrayLiteralNode arrayLiteral)
                {
                    var arrayLlvmType = ResolveType(resolvedType);
                    var alloca = _builder.BuildAlloca(arrayLlvmType, varDecl.Name);
                    EmitArrayLiteralElements(arrayLiteral, alloca, arrayLlvmType);
                    _currentScope.Declare(
                        varDecl.Name,
                        new VariableSymbol(resolvedType, arrayLlvmType, alloca));
                    break;
                }

                var initializer = GenerateExpression(varDecl.Initializer);

                initializer = CastInitializer(
                    initializer,
                    varDecl.Initializer,
                    declaredType,
                    resolvedType,
                    $"init_{varDecl.Name}");

                var scalarAlloca = _builder.BuildAlloca(declaredType, varDecl.Name);
                _builder.BuildStore(initializer, scalarAlloca);
                _currentScope.Declare(varDecl.Name, new VariableSymbol(resolvedType, declaredType, scalarAlloca));

                break;
            }

            case TargetAssignmentStatementNode targetAssignment:
            {
                var (targetPtr, targetLumiType, targetLlvmType) =
                    ResolveTargetAddress(targetAssignment.Target);

                var value = GenerateExpression(targetAssignment.Value);
                value = CastInitializer(
                    value,
                    targetAssignment.Value,
                    targetLlvmType,
                    targetLumiType,
                    "assign_target");

                _builder.BuildStore(value, targetPtr);
                break;
            }

            case ExpressionStatementNode exprStmt:
                GenerateExpression(exprStmt.Expression);
                break;

            case ReturnStatementNode returnStatement:
            {
                if (returnStatement.Expression is null)
                {
                    _builder.BuildRetVoid();
                    break;
                }

                var returnValue = GenerateExpression(returnStatement.Expression);
                var expectedTypeName = _semanticModel.GetType(returnStatement);
                var expectedType = ResolveType(expectedTypeName);

                returnValue = CastInitializer(
                    returnValue,
                    returnStatement.Expression,
                    expectedType,
                    expectedTypeName,
                    "ret_cast");

                _builder.BuildRet(returnValue);
                break;
            }

            case PrintStatementNode printStmt:
                var printValue = GenerateExpression(printStmt.Expression);
                var expressionType = _semanticModel.GetExpressionType(printStmt.Expression);

                switch (expressionType)
                {
                    case TypeConst.Char:
                        WriterUtils.EmitPrintChar(printValue, _module, _context, _builder);
                        break;
                    case "i8" or "i16" or "i32" or "i64"
                        or "u8" or "u16" or "u32" or "u64":
                        WriterUtils.EmitPrintInt(printValue, _module, _context, _builder);
                        break;
                    case TypeConst.Bool:
                        WriterUtils.EmitPrintBool(printValue, _module, _context, _builder);
                        break;
                    case TypeConst.F16 or TypeConst.F32 or TypeConst.F64:
                        WriterUtils.EmitPrintFloat(printValue, _module, _context, _builder);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Printing values of type '{expressionType}' is not supported.");
                }

                if (printStmt.AppendNewLine)
                {
                    WriterUtils.EmitSystemWrite(
                        "\n", _module, _context, _builder);
                }

                break;

            case IfStatementNode ifStmt:
            {
                GenerateIfStatement(ifStmt);
                break;
            }

            case WhileStatementNode whileStmt:
            {
                GenerateWhileStatement(whileStmt);
                break;
            }

            case AssignmentStatementNode assignment:
            {
                var variable = _currentScope.ResolveVariable(assignment.Name);
                var value = GenerateExpression(assignment.Value);

                value = CastInitializer(
                    value,
                    assignment.Value,
                    variable.LLVMType,
                    variable.TypeName,
                    $"assign_{assignment.Name}");

                _builder.BuildStore(value, variable.Allocation);

                break;
            }
            case BreakStatementNode:
                GenerateBreakStatement();
                break;

            case ContinueStatementNode:
                GenerateContinueStatement();
                break;

            case ForStatementNode forStmt:
            {
                GenerateForStatement(forStmt);
                break;
            }

            default:
            {
                throw new NotSupportedException(
                    $"Code generation not implemented for {statement.GetType().Name}");
            }
        }
    }

    private void GenerateScopedStatements(
        IReadOnlyList<StatementNode> statements)
    {
        _currentScope = _currentScope.EnterScope();

        try
        {
            GenerateStatements(statements);
        }
        finally
        {
            _currentScope = _currentScope.ExitScope();
        }
    }

    private void GenerateForStatement(ForStatementNode forStmt)
    {
        // The initializer variable belongs to the for-loop scope.
        _currentScope = _currentScope.EnterScope();

        try
        {
            // Initializer: execute exactly once.
            if (forStmt.Initializer is not null)
            {
                GenerateStatement(forStmt.Initializer);
            }

            var function = _builder.InsertBlock.Parent;

            var conditionBlock = _context.AppendBasicBlock(
                function, "for.condition");

            var bodyBlock = _context.AppendBasicBlock(
                function, "for.body");

            var updateBlock = _context.AppendBasicBlock(
                function, "for.update");

            var exitBlock = _context.AppendBasicBlock(
                function, "for.exit");

            // Enter the loop.
            _builder.BuildBr(conditionBlock);

            // Condition.
            _builder.PositionAtEnd(conditionBlock);

            if (forStmt.Condition is not null)
            {
                var condition = GenerateExpression(forStmt.Condition);

                if (condition.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
                    condition.TypeOf.IntWidth != 1)
                {
                    throw new InvalidOperationException(
                        "For loop condition must be a boolean.");
                }

                _builder.BuildCondBr(
                    condition,
                    bodyBlock,
                    exitBlock);
            }
            else
            {
                // An omitted condition means an infinite loop.
                _builder.BuildBr(bodyBlock);
            }

            // Register break and continue targets.
            _loopTargets.Push(new LoopTargets(
                exitBlock,
                updateBlock));

            try
            {
                // Body.
                _builder.PositionAtEnd(bodyBlock);

                GenerateStatements(forStmt.Body);

                if (!HasTerminator(_builder.InsertBlock))
                {
                    _builder.BuildBr(updateBlock);
                }

                // Update.
                _builder.PositionAtEnd(updateBlock);

                if (forStmt.Update is not null)
                {
                    GenerateStatement(forStmt.Update);
                }

                if (!HasTerminator(_builder.InsertBlock))
                {
                    _builder.BuildBr(conditionBlock);
                }
            }
            finally
            {
                _loopTargets.Pop();
            }

            // Continue after the loop.
            _builder.PositionAtEnd(exitBlock);
        }
        finally
        {
            // Restore the enclosing lexical scope.
            _currentScope = _currentScope.ExitScope();
        }
    }

    private void GenerateWhileStatement(WhileStatementNode whileStmt)
    {
        var function = _builder.InsertBlock.Parent;

        var conditionBlock = _context.AppendBasicBlock(function, "while.condition");
        var bodyBlock = _context.AppendBasicBlock(function, "while.body");
        var exitBlock = _context.AppendBasicBlock(function, "while.exit");

        // Enter the loop.
        _builder.BuildBr(conditionBlock);

        // Evaluate the condition.
        _builder.PositionAtEnd(conditionBlock);

        var condition = GenerateExpression(whileStmt.Condition);

        _builder.BuildCondBr(condition, bodyBlock, exitBlock);

        // Register targets for break and continue.
        _loopTargets.Push(new LoopTargets(
            exitBlock,
            conditionBlock));

        try
        {
            _builder.PositionAtEnd(bodyBlock);

            GenerateScopedStatements(whileStmt.Body);

            // Return to the condition if the body hasn't terminated.
            if (_builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
            {
                _builder.BuildBr(conditionBlock);
            }
        }
        finally
        {
            _loopTargets.Pop();
        }

        // Continue generating after the loop.
        _builder.PositionAtEnd(exitBlock);
    }

    private void GenerateIfStatement(IfStatementNode statement)
    {
        var condition = GenerateExpression(statement.Condition);

        if (condition.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            condition.TypeOf.IntWidth != 1)
        {
            throw new InvalidOperationException(
                "If condition must be a boolean.");
        }

        var function = _builder.InsertBlock.Parent;

        var thenBlock = _context.AppendBasicBlock(function, "if.then");
        var mergeBlock = _context.AppendBasicBlock(function, "if.end");

        LLVMBasicBlockRef elseBlock = default;

        if (statement.ElseBody is not null)
        {
            elseBlock = _context.AppendBasicBlock(function, "if.else");
        }

        _builder.BuildCondBr(
            condition,
            thenBlock,
            statement.ElseBody is not null ? elseBlock : mergeBlock);

        // Generate the then branch in its own lexical scope.
        _builder.PositionAtEnd(thenBlock);

        _currentScope = _currentScope.EnterScope();

        try
        {
            GenerateStatements(statement.ThenBody);
        }
        finally
        {
            _currentScope = _currentScope.ExitScope();
        }

        var thenEndBlock = _builder.InsertBlock;

        var thenCanContinue = thenEndBlock.Terminator.Handle == IntPtr.Zero;

        if (thenCanContinue)
        {
            _builder.BuildBr(mergeBlock);
        }

        // Generate the else branch, if present, in its own lexical scope.
        var elseCanContinue = statement.ElseBody is null;

        if (statement.ElseBody is not null)
        {
            _builder.PositionAtEnd(elseBlock);

            _currentScope = _currentScope.EnterScope();

            try
            {
                GenerateStatements(statement.ElseBody);
            }
            finally
            {
                _currentScope = _currentScope.ExitScope();
            }

            var elseEndBlock = _builder.InsertBlock;

            elseCanContinue = elseEndBlock.Terminator.Handle == IntPtr.Zero;

            if (elseCanContinue)
            {
                _builder.BuildBr(mergeBlock);
            }
        }

        // If neither branch can reach the merge block, remove it.
        if (!thenCanContinue && !elseCanContinue)
        {
            mergeBlock.Delete();

            _builder.PositionAtEnd(thenEndBlock);
            return;
        }

        _builder.PositionAtEnd(mergeBlock);
    }

    private void GenerateBreakStatement()
    {
        if (_loopTargets.Count == 0)
        {
            throw new InvalidOperationException(
                "'break' can only be used inside a loop.");
        }

        _builder.BuildBr(_loopTargets.Peek().BreakTarget);
    }

    private void GenerateContinueStatement()
    {
        if (_loopTargets.Count == 0)
        {
            throw new InvalidOperationException(
                "'continue' can only be used inside a loop.");
        }

        _builder.BuildBr(_loopTargets.Peek().ContinueTarget);
    }

    private static bool HasTerminator(LLVMBasicBlockRef block)
    {
        var lastInstruction = block.LastInstruction;

        if (lastInstruction.IsNull)
        {
            return false;
        }

        return lastInstruction.InstructionOpcode is
            LLVMOpcode.LLVMRet or
            LLVMOpcode.LLVMBr or
            LLVMOpcode.LLVMSwitch or
            LLVMOpcode.LLVMIndirectBr or
            LLVMOpcode.LLVMInvoke or
            LLVMOpcode.LLVMUnreachable or
            LLVMOpcode.LLVMResume or
            LLVMOpcode.LLVMCleanupRet or
            LLVMOpcode.LLVMCatchRet or
            LLVMOpcode.LLVMCatchSwitch;
    }


    private void GenerateStatements(
        IReadOnlyList<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            var currentBlock = _builder.InsertBlock;

            // An empty block has no instructions, so it cannot have a terminator.
            var lastInstruction = currentBlock.LastInstruction;

            if (!lastInstruction.IsNull)
            {
                // Stop only if the last instruction is a terminator.
                var opcode = lastInstruction.InstructionOpcode;

                if (opcode is LLVMOpcode.LLVMRet
                    or LLVMOpcode.LLVMBr
                    or LLVMOpcode.LLVMSwitch
                    or LLVMOpcode.LLVMIndirectBr
                    or LLVMOpcode.LLVMInvoke
                    or LLVMOpcode.LLVMUnreachable
                    or LLVMOpcode.LLVMResume
                    or LLVMOpcode.LLVMCleanupRet
                    or LLVMOpcode.LLVMCatchRet
                    or LLVMOpcode.LLVMCatchSwitch)
                {
                    break;
                }
            }

            GenerateStatement(statement);
        }
    }

    private LLVMValueRef GenerateExpression(ExpressionNode expression)
    {
        return expression switch
        {
            LiteralNode<string> num => LLVMValueRef.CreateConstInt(
                ResolveType(_semanticModel.GetExpressionType(expression)),
                ulong.Parse(num.Value)),
            LiteralNode<double> floating => LLVMValueRef.CreateConstReal(
                ResolveType(_semanticModel.GetExpressionType(expression)),
                floating.Value),
            IdentifierNode id => LoadVariable(id.Name),
            BinaryExpressionNode binary => GenerateBinary(binary),
            UnaryExpressionNode unary => GenerateUnary(unary),
            LiteralNode<char> character => LLVMValueRef.CreateConstInt(_context.Int8Type, character.Value),
            LiteralNode<bool> boolean => LLVMValueRef.CreateConstInt(_context.Int1Type, (ulong)(boolean.Value ? 1 : 0)),
            StructLiteralNode structLiteral => GenerateStructLiteral(structLiteral),
            ArrayLiteralNode arrayLiteral => GenerateArrayLiteral(arrayLiteral),
            IndexExpressionNode indexExpression => GenerateIndexExpression(indexExpression),
            FunctionCallExpressionNode functionCall => GenerateFunctionCall(functionCall),
            MemberAccessNode member => GenerateMemberAccess(member),
            _ => throw new Exception("Unknown IR generation node target.")
        };
    }

    private LLVMValueRef GenerateStructLiteral(StructLiteralNode literal)
    {
        var typeName = _semanticModel.GetExpressionType(literal);
        var mangledName = _semanticModel.GetMangledName(literal);
        // If it's a struct literal, the expression type might be the original name, but the layout is stored under mangled name.
        // Actually TypeChecker.CheckStructLiteral sets the expression type.
        // Let's try mangled name first, then typeName.
        if (!_structLayouts.TryGetValue(mangledName, out var structLayout) &&
            !_structLayouts.TryGetValue(typeName, out structLayout))
        {
            throw new Exception($"Codegen Error: Struct layout for '{typeName}' (mangled: '{mangledName}') not found.");
        }

        var alloca = _builder.BuildAlloca(structLayout.StructType, "struct_literal");
        EmitStructLiteralFields(literal, alloca, structLayout);
        return _builder.BuildLoad2(structLayout.StructType, alloca, "struct_literal_load");
    }

    private LLVMValueRef GenerateArrayLiteral(ArrayLiteralNode literal)
    {
        var typeName = _semanticModel.GetExpressionType(literal);
        var llvmType = ResolveType(typeName);
        var alloca = _builder.BuildAlloca(llvmType, "array_literal");

        EmitArrayLiteralElements(literal, alloca, llvmType);

        return _builder.BuildLoad2(llvmType, alloca, "array_literal_load");
    }

    private void EmitArrayLiteralElements(ArrayLiteralNode literal, LLVMValueRef arrayPtr, LLVMTypeRef arrayType)
    {
        var elementType = arrayType.ElementType;
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            var elementValue = GenerateExpression(literal.Elements[i]);
            var elementLumiType = _semanticModel.GetExpressionType(literal.Elements[i]);

            var indices = new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (uint)i)
            };

            var elementPtr = _builder.BuildGEP2(arrayType, arrayPtr, indices, $"array_literal_idx_{i}");

            // Handle type mismatch (e.g., constant promotion)
            elementValue = CastInitializer(
                elementValue,
                literal.Elements[i],
                elementType,
                "", // targetTypeName is not used for integers anyway
                $"array_literal_init_{i}");

            _builder.BuildStore(elementValue, elementPtr);
        }
    }

    private LLVMValueRef GenerateIndexExpression(IndexExpressionNode indexExpr)
    {
        var (ptr, _, elementType) = ResolveTargetAddress(indexExpr);
        return _builder.BuildLoad2(elementType, ptr, "index_load");
    }

    private LLVMValueRef GenerateMemberAccess(MemberAccessNode member)
    {
        var (fieldPtr, _, fieldLlvmType) = ResolveTargetAddress(member);
        return _builder.BuildLoad2(fieldLlvmType, fieldPtr, $"load_{member.MemberName}");
    }

    private LLVMValueRef GenerateFunctionCall(
        FunctionCallExpressionNode functionCall)
    {
        var mangledName = _semanticModel.GetMangledName(functionCall);
        if (!_functions.TryGetValue(mangledName, out var functionInfo))
        {
            throw new Exception(
                $"Codegen Error: Function '{functionCall.Name}' (mangled: '{mangledName}') was not declared.");
        }

        if (!_semanticModel.Functions.TryGet(mangledName, out var signature))
        {
            throw new Exception(
                $"Codegen Error: Function '{functionCall.Name}' (mangled: '{mangledName}') was not found in SemanticModel.");
        }

        var arguments = new LLVMValueRef[functionCall.Arguments.Count];
        for (var i = 0; i < functionCall.Arguments.Count; i++)
        {
            var argExpr = functionCall.Arguments[i];
            var argValue = GenerateExpression(argExpr);
            var targetTypeName = signature.ParameterTypes[i];
            var targetType = ResolveType(targetTypeName);

            arguments[i] = CastInitializer(
                argValue,
                argExpr,
                targetType,
                targetTypeName,
                $"arg_{i}");
        }

        var returnType = functionInfo.FunctionType.ReturnType;

        var name = returnType.Kind == LLVMTypeKind.LLVMVoidTypeKind
            ? string.Empty
            : "calltmp";

        return _builder.BuildCall2(
            functionInfo.FunctionType,
            functionInfo.Function,
            arguments,
            name);
    }

    private LLVMValueRef GenerateUnary(UnaryExpressionNode unary)
    {
        var operand = GenerateExpression(unary.Operand);

        return unary.Operator switch
        {
            "-" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNeg(operand, "negtmp"),

            "-" when operand.TypeOf.Kind is
                    LLVMTypeKind.LLVMHalfTypeKind
                    or LLVMTypeKind.LLVMFloatTypeKind
                    or LLVMTypeKind.LLVMDoubleTypeKind =>
                _builder.BuildFNeg(operand, "fnegtmp"),

            "!" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNot(operand, "logical.not"),

            "~" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNot(operand, "bitwise.not"),

            "-" => throw new NotSupportedException(
                "Unary '-' requires a numeric operand."),

            _ => throw new NotSupportedException(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
    }

    private LLVMValueRef LoadVariable(string name)
    {
        var variable = _currentScope.ResolveVariable(name);

        return _builder.BuildLoad2(
            variable.LLVMType,
            variable.Allocation,
            name);
    }

    private LLVMValueRef GenerateBinary(BinaryExpressionNode binary)
    {
        if (binary.Operator is TypeConst.LogicalAnd or TypeConst.LogicalOr)
        {
            return GenerateLogicalBinary(binary);
        }

        var left = GenerateExpression(binary.Left);
        var right = GenerateExpression(binary.Right);

        if (TypeChecker.IsShiftOperator(binary.Operator))
        {
            return GenerateShift(binary, left, right);
        }

        if (left.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            || right.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
        {
            throw new NotSupportedException(
                "Pointer arithmetic is not supported.");
        }

        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        if (leftType == TypeConst.Bool && rightType == TypeConst.Bool)
        {
            if (binary.Operator is TypeConst.EqualsEquals or TypeConst.BangEquals)
            {
                var predicate = binary.Operator switch
                {
                    TypeConst.EqualsEquals => LLVMIntPredicate.LLVMIntEQ,
                    TypeConst.BangEquals => LLVMIntPredicate.LLVMIntNE,

                    _ => throw new NotSupportedException(
                        $"Unsupported boolean operator: {binary.Operator}")
                };

                return _builder.BuildICmp(
                    predicate,
                    left,
                    right,
                    "bool.cmptmp");
            }

            throw new NotSupportedException(
                $"Operator '{binary.Operator}' is not supported for boolean operands.");
        }

        if (IsFloatingPoint(left.TypeOf.Kind)
            || IsFloatingPoint(right.TypeOf.Kind))
        {
            if (!IsFloatingPoint(left.TypeOf.Kind)
                || !IsFloatingPoint(right.TypeOf.Kind)
                || left.TypeOf != right.TypeOf)
            {
                throw CreateIncompatibleOperandsException(
                    leftType,
                    rightType);
            }

            if (IsComparisonOperator(binary.Operator))
            {
                var predicate = binary.Operator switch
                {
                    TypeConst.EqualsEquals =>
                        LLVMRealPredicate.LLVMRealOEQ,

                    TypeConst.BangEquals =>
                        LLVMRealPredicate.LLVMRealUNE,

                    TypeConst.LessThan =>
                        LLVMRealPredicate.LLVMRealOLT,

                    TypeConst.LessThanOrEquals =>
                        LLVMRealPredicate.LLVMRealOLE,

                    TypeConst.GreaterThan =>
                        LLVMRealPredicate.LLVMRealOGT,

                    TypeConst.GreaterThanOrEquals =>
                        LLVMRealPredicate.LLVMRealOGE,

                    _ => throw new NotSupportedException(
                        $"Unsupported floating-point comparison: {binary.Operator}")
                };

                return _builder.BuildFCmp(
                    predicate,
                    left,
                    right,
                    "fcmp");
            }

            return binary.Operator switch
            {
                TypeConst.Plus =>
                    _builder.BuildFAdd(left, right, "faddtmp"),

                TypeConst.Minus =>
                    _builder.BuildFSub(left, right, "fsubtmp"),

                TypeConst.Star =>
                    _builder.BuildFMul(left, right, "fmultmp"),

                TypeConst.Slash =>
                    _builder.BuildFDiv(left, right, "fdivtmp"),

                _ => throw new NotSupportedException(
                    $"Unsupported floating-point operator: {binary.Operator}")
            };
        }

        if (left.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind
            || right.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            throw new NotSupportedException(
                $"Binary operator '{binary.Operator}' requires numeric operands.");
        }

        var promoted = PromoteIntegerOperands(
            left,
            right,
            leftType,
            rightType);

        left = promoted.Left;
        right = promoted.Right;

        var commonType = promoted.CommonType;

        if (IsComparisonOperator(binary.Operator))
        {
            var predicate = binary.Operator switch
            {
                TypeConst.EqualsEquals =>
                    LLVMIntPredicate.LLVMIntEQ,

                TypeConst.BangEquals =>
                    LLVMIntPredicate.LLVMIntNE,

                TypeConst.LessThan =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntULT
                        : LLVMIntPredicate.LLVMIntSLT,

                TypeConst.LessThanOrEquals =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntULE
                        : LLVMIntPredicate.LLVMIntSLE,

                TypeConst.GreaterThan =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntUGT
                        : LLVMIntPredicate.LLVMIntSGT,

                TypeConst.GreaterThanOrEquals =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntUGE
                        : LLVMIntPredicate.LLVMIntSGE,

                _ => throw new NotSupportedException(
                    $"Unsupported integer comparison: {binary.Operator}")
            };

            return _builder.BuildICmp(
                predicate,
                left,
                right,
                "cmptmp");
        }

        return binary.Operator switch
        {
            TypeConst.Plus =>
                _builder.BuildAdd(left, right, "addtmp"),

            TypeConst.Minus =>
                _builder.BuildSub(left, right, "subtmp"),

            TypeConst.Star =>
                _builder.BuildMul(left, right, "multmp"),

            // Bitwise operators
            TypeConst.Ampersand => _builder.BuildAnd(left, right, "bitwise.and"),
            TypeConst.Pipe => _builder.BuildOr(left, right, "bitwise.or"),
            TypeConst.Caret => _builder.BuildXor(left, right, "bitwise.xor"),

            TypeConst.ShiftLeft => _builder.BuildShl(left, right, "shift.left"),
            TypeConst.ShiftRight when IsUnsignedIntegerType(binary.Left) =>
                _builder.BuildLShr(left, right, "shift.right"),

            TypeConst.ShiftRight => _builder.BuildAShr(left, right, "shift.right"),

            TypeConst.Slash =>
                TypeChecker.IsUnsignedInteger(commonType)
                    ? _builder.BuildUDiv(left, right, "divtmp")
                    : _builder.BuildSDiv(left, right, "divtmp"),

            _ => throw new NotSupportedException(
                $"Unsupported integer operator: {binary.Operator}")
        };
    }

    private bool IsUnsignedIntegerType(ExpressionNode expression)
    {
        var type = _semanticModel.GetExpressionType(expression);
        return TypeChecker.IsUnsignedInteger(type);
    }

    private LLVMValueRef GenerateLogicalBinary(BinaryExpressionNode binary)
    {
        var isAnd = binary.Operator == TypeConst.LogicalAnd;

        var left = GenerateExpression(binary.Left);
        var leftEndBlock = _builder.InsertBlock;

        var function = leftEndBlock.Parent;

        var rightBlock = _context.AppendBasicBlock(
            function,
            isAnd ? "and.rhs" : "or.rhs");

        var mergeBlock = _context.AppendBasicBlock(
            function,
            isAnd ? "and.end" : "or.end");

        if (isAnd)
        {
            _builder.BuildCondBr(left, rightBlock, mergeBlock);
        }
        else
        {
            _builder.BuildCondBr(left, mergeBlock, rightBlock);
        }

        _builder.PositionAtEnd(rightBlock);

        var right = GenerateExpression(binary.Right);
        var rightEndBlock = _builder.InsertBlock;

        if (!HasTerminator(rightEndBlock))
        {
            _builder.BuildBr(mergeBlock);
        }

        _builder.PositionAtEnd(mergeBlock);

        var shortCircuitValue = LLVMValueRef.CreateConstInt(
            _context.Int1Type,
            isAnd ? 0UL : 1UL);

        var phi = _builder.BuildPhi(_context.Int1Type, "logical.result");

        phi.AddIncoming(
            [shortCircuitValue, right],
            [leftEndBlock, rightEndBlock],
            2);

        return phi;
    }

    private (LLVMValueRef Left, LLVMValueRef Right, string CommonType) PromoteIntegerOperands(
        LLVMValueRef left,
        LLVMValueRef right,
        string leftType,
        string rightType)
    {
        var commonType = TypePromoter.GetCommonType(
            leftType,
            rightType);

        if (commonType is null)
        {
            throw CreateIncompatibleOperandsException(
                leftType,
                rightType);
        }

        left = _llvmTypeHelper.ConvertInteger(
            left,
            leftType,
            commonType);

        right = _llvmTypeHelper.ConvertInteger(
            right,
            rightType,
            commonType);

        return (left, right, commonType);
    }

    private static bool IsFloatingPoint(LLVMTypeKind kind)
    {
        return kind is LLVMTypeKind.LLVMHalfTypeKind
            or LLVMTypeKind.LLVMFloatTypeKind
            or LLVMTypeKind.LLVMDoubleTypeKind;
    }

    private static bool IsComparisonOperator(string op)
    {
        return op is TypeConst.EqualsEquals
            or TypeConst.BangEquals
            or TypeConst.LessThan
            or TypeConst.LessThanOrEquals
            or TypeConst.GreaterThan
            or TypeConst.GreaterThanOrEquals;
    }

    private static InvalidOperationException CreateIncompatibleOperandsException(
        string leftType,
        string rightType)
    {
        return new InvalidOperationException(
            $"Codegen received incompatible operand types: " +
            $"'{leftType}' and '{rightType}'.");
    }

    private LLVMTypeRef ResolveType(string typeName)
    {
        if (typeName.Contains('[') && typeName.EndsWith(']'))
        {
            var lastBracket = typeName.LastIndexOf('[');
            var elementTypeName = typeName.Substring(0, lastBracket);
            var lengthStr = typeName.Substring(lastBracket + 1, typeName.Length - lastBracket - 2);
            var length = uint.Parse(lengthStr);
            var elementType = ResolveType(elementTypeName);
            return LLVMTypeRef.CreateArray(elementType, length);
        }

        if (_structLayouts.TryGetValue(typeName, out var structLayout))
        {
            return structLayout.StructType;
        }

        // Try mangled name
        var mangled = typeName.Replace("::", "_");
        if (_structLayouts.TryGetValue(mangled, out structLayout))
        {
            return structLayout.StructType;
        }

        return typeName switch
        {
            TypeConst.I8 or TypeConst.U8 => _context.Int8Type,
            TypeConst.I16 or TypeConst.U16 => _context.Int16Type,
            TypeConst.I32 or TypeConst.U32 => _context.Int32Type,
            TypeConst.I64 or TypeConst.U64 => _context.Int64Type,

            TypeConst.Char => _context.Int8Type,
            TypeConst.Void => _context.VoidType,
            TypeConst.Bool => _context.Int1Type,

            TypeConst.F16 => _context.HalfType,
            TypeConst.F32 => _context.FloatType,
            TypeConst.F64 => _context.DoubleType,

            _ => throw new NotSupportedException(
                $"Unsupported backend data type: {typeName}")
        };
    }

    public void DumpModuleToConsole()
    {
        _module.Dump();
    }

    public void CompileToNativeObject(string outputFileName)
    {
        // Fetch the default target triple for your machine
        var targetTriple = LLVMTargetRef.DefaultTriple;
        _module.Target = targetTriple;

        // 2. Locate the specific target compilation engine
        var target = LLVMTargetRef.GetTargetFromTriple(targetTriple);

        // 3. Create the Target Machine profile configuration
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

        // 4. Emit the compilation tree into a native binary object file (.o)
        targetMachine.EmitToFile(
            _module,
            outputFileName,
            LLVMCodeGenFileType.LLVMObjectFile
        );

        Console.WriteLine($"[Codegen]: Object file written successfully to: {outputFileName}");
    }

    private void EmitStructLiteralFields(
        StructLiteralNode literal,
        LLVMValueRef structAlloca,
        StructLayoutInfo layout)
    {
        foreach (var init in literal.Initializers)
        {
            if (!layout.FieldsByName.TryGetValue(init.FieldName, out var field))
            {
                throw new InvalidOperationException(
                    $"Unknown field '{init.FieldName}' on struct '{layout.Name}'.");
            }

            var fieldValue = GenerateExpression(init.Value);
            fieldValue = CastInitializer(
                fieldValue,
                init.Value,
                field.LlvmType,
                field.LumiType,
                $"field_{layout.Name}_{field.Name}");

            var fieldPtr = _builder.BuildStructGEP2(
                layout.StructType,
                structAlloca,
                (uint)field.Index,
                $"{layout.Name}.{field.Name}.ptr");

            _builder.BuildStore(fieldValue, fieldPtr);
        }
    }

    private (LLVMValueRef Pointer, string LumiType, LLVMTypeRef LlvmType) ResolveTargetAddress(
        ExpressionNode target)
    {
        switch (target)
        {
            case IdentifierNode id:
            {
                var variable = _currentScope.ResolveVariable(id.Name);
                return (variable.Allocation, variable.TypeName, variable.LLVMType);
            }

            case MemberAccessNode member:
            {
                var (basePtr, baseLumiType, _) = ResolveTargetAddress(member.Target);

                if (!_structLayouts.TryGetValue(baseLumiType, out var layout))
                {
                    throw new InvalidOperationException(
                        $"Type '{baseLumiType}' is not a struct.");
                }

                if (!layout.FieldsByName.TryGetValue(member.MemberName, out var field))
                {
                    throw new InvalidOperationException(
                        $"Struct '{baseLumiType}' has no field '{member.MemberName}'.");
                }

                var fieldPtr = _builder.BuildStructGEP2(
                    layout.StructType,
                    basePtr,
                    (uint)field.Index,
                    $"{baseLumiType}.{field.Name}.ptr");

                return (fieldPtr, field.LumiType, field.LlvmType);
            }

            case IndexExpressionNode indexExpr:
            {
                var (basePtr, baseLumiType, baseLlvmType) = ResolveTargetAddress(indexExpr.Target);

                var indexValue = GenerateExpression(indexExpr.Index);

                var indices = new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    indexValue
                };

                var elementPtr = _builder.BuildGEP2(
                    baseLlvmType,
                    basePtr,
                    indices,
                    "array_index_ptr");

                var elementType = baseLlvmType.ElementType;
                var elementLumiType = _semanticModel.GetExpressionType(indexExpr);

                return (elementPtr, elementLumiType, elementType);
            }

            default:
                throw new NotSupportedException(
                    $"Unsupported member/assignment target '{target.GetType().Name}'.");
        }
    }

    private LLVMValueRef CastInteger(
        LLVMValueRef value,
        LLVMTypeRef targetType,
        string name)
    {
        var sourceType = value.TypeOf;

        if (sourceType.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            targetType.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (sourceType == targetType)
            {
                return value;
            }

            throw new NotSupportedException(
                $"Cannot convert {sourceType} to {targetType}.");
        }

        if (sourceType.IntWidth == targetType.IntWidth)
        {
            return value;
        }

        if (sourceType.IntWidth < targetType.IntWidth)
        {
            return _builder.BuildSExt(
                value,
                targetType,
                name);
        }

        return _builder.BuildTrunc(
            value,
            targetType,
            name);
    }


    private LLVMValueRef CastInitializer(
        LLVMValueRef value,
        ExpressionNode expression,
        LLVMTypeRef targetType,
        string targetTypeName,
        string name)
    {
        var sourceType = value.TypeOf;

        if (sourceType.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            targetType.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (sourceType == targetType)
            {
                return value;
            }

            throw new NotSupportedException(
                $"Cannot convert initializer to '{targetTypeName}'.");
        }

        var sourceWidth = sourceType.IntWidth;
        var targetWidth = targetType.IntWidth;

        if (sourceWidth == targetWidth)
        {
            return value;
        }

        if (sourceWidth > targetWidth)
        {
            return _builder.BuildTrunc(value, targetType, name);
        }

        // Integer literals currently represent non-negative values.
        // Their magnitude is preserved by zero extension.
        if (expression is LiteralNode<string>)
        {
            return _builder.BuildZExt(value, targetType, name);
        }

        // For variables, preserve their declared signedness.

        if (expression is IdentifierNode identifier)
        {
            var variable = _currentScope.ResolveVariable(identifier.Name);

            return variable.IsUnsignedIntegerType()
                ? _builder.BuildZExt(value, targetType, name)
                : _builder.BuildSExt(value, targetType, name);
        }

        // Fallback for integer expressions.
        return _builder.BuildSExt(value, targetType, name);
    }

    private LLVMValueRef GenerateShift(
        BinaryExpressionNode binary,
        LLVMValueRef left,
        LLVMValueRef right)
    {
        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        var bitWidth = left.TypeOf.IntWidth;
        var isUnsignedShiftAmount =
            TypeChecker.IsUnsignedInteger(rightType);

        var function = _builder.InsertBlock.Parent;

        var validBlock = _context.AppendBasicBlock(function, "shift.valid");
        var invalidBlock = _context.AppendBasicBlock(function, "shift.invalid");

        // Reject negative shift amounts for signed integers.
        LLVMValueRef isValid;

        if (isUnsignedShiftAmount)
        {
            isValid = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntULT,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, bitWidth),
                "shift.amount.valid");
        }
        else
        {
            var isNonNegative = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntSGE,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, 0),
                "shift.amount.nonnegative");

            var isBelowWidth = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntULT,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, bitWidth),
                "shift.amount.below.width");

            isValid = _builder.BuildAnd(
                isNonNegative,
                isBelowWidth,
                "shift.amount.valid");
        }

        _builder.BuildCondBr(isValid, validBlock, invalidBlock);

        // Invalid shifts terminate the program.
        _builder.PositionAtEnd(invalidBlock);
        _builder.BuildCall2(
            LLVMTypeRef.CreateFunction(_context.VoidType, []),
            _module.AddFunction("abort", LLVMTypeRef.CreateFunction(_context.VoidType, [])),
            []);

        _builder.BuildUnreachable();

        // Generate the actual shift only on the valid path.
        _builder.PositionAtEnd(validBlock);

        return binary.Operator switch
        {
            TypeConst.ShiftLeft =>
                _builder.BuildShl(left, right, "shift.left"),

            TypeConst.ShiftRight when IsUnsignedIntegerType(binary.Left) =>
                _builder.BuildLShr(left, right, "shift.right"),

            TypeConst.ShiftRight =>
                _builder.BuildAShr(left, right, "shift.right"),

            _ => throw new NotSupportedException(
                $"Unsupported shift operator: '{binary.Operator}'.")
        };
    }

    private void RegisterStructs(ProgramNode program)
    {
        var namedTypes = new Dictionary<string, LLVMTypeRef>();

        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var llvmStruct = _context.CreateNamedStruct(mangledName);
            namedTypes[mangledName] = llvmStruct;
        }

        // Store partial/opaque types first so nested struct fields can resolve them
        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var fieldsByName = new Dictionary<string, StructFieldLayout>();
            var orderedFields = new List<StructFieldLayout>();

            for (var i = 0; i < structNode.Fields.Count; i++)
            {
                var field = structNode.Fields[i];
                var layout = new StructFieldLayout(i, field.Name, field.Type, default);
                orderedFields.Add(layout);
                fieldsByName[field.Name] = layout;
            }

            _structLayouts[mangledName] = new StructLayoutInfo(
                mangledName,
                namedTypes[mangledName],
                orderedFields,
                fieldsByName);
        }

        // Set struct bodies now that every struct type exists
        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var info = _structLayouts[mangledName];
            var fieldLlvmTypes = new LLVMTypeRef[structNode.Fields.Count];
            var updatedOrdered = new List<StructFieldLayout>(structNode.Fields.Count);
            var updatedByName = new Dictionary<string, StructFieldLayout>(structNode.Fields.Count);

            for (var i = 0; i < structNode.Fields.Count; i++)
            {
                var field = structNode.Fields[i];
                var fieldLlvmType = ResolveType(field.Type);
                fieldLlvmTypes[i] = fieldLlvmType;

                var layout = new StructFieldLayout(i, field.Name, field.Type, fieldLlvmType);
                updatedOrdered.Add(layout);
                updatedByName[field.Name] = layout;
            }

            info.StructType.StructSetBody(fieldLlvmTypes, false);

            _structLayouts[mangledName] = new StructLayoutInfo(
                mangledName,
                info.StructType,
                updatedOrdered,
                updatedByName);
        }
    }

    private sealed record LoopTargets(
        LLVMBasicBlockRef BreakTarget,
        LLVMBasicBlockRef ContinueTarget);
}
