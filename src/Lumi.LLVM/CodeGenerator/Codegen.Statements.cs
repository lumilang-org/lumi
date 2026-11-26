using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
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

            case TupleDestructuringNode destructuring:
                GenerateTupleDestructuring(destructuring);
                break;

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

                // Convert to the declared return type, not the expression's own type
                // (e.g. `return -1` is an i8 expression in an i32 function).
                returnValue = CastInitializer(
                    returnValue,
                    returnStatement.Expression,
                    _currentReturnType,
                    _currentReturnTypeName,
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
                    case "i8" or "i16" or "i32" or "i64":
                        WriterUtils.EmitPrintInt(printValue, _module, _context, _builder);
                        break;
                    case TypeConst.U8 or TypeConst.U16 or TypeConst.U32 or TypeConst.U64 or TypeConst.Usize:
                        WriterUtils.EmitPrintInt(printValue, _module, _context, _builder, true);
                        break;
                    case TypeConst.String:
                        WriterUtils.EmitPrintString(printValue, _module, _context, _builder);
                        break;
                    case TypeConst.Bool:
                        WriterUtils.EmitPrintBool(printValue, _module, _context, _builder);
                        break;
                    case TypeConst.F16 or TypeConst.F32 or TypeConst.F64:
                        WriterUtils.EmitPrintFloat(printValue, _module, _context, _builder);
                        break;
                    case var pointerType when PointerTypes.IsPointer(pointerType):
                        // Prints the address held by the pointer, not the pointee.
                        WriterUtils.EmitPrintPointer(printValue, _module, _context, _builder);
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
            case SwitchStatementNode switchStmt:
                GenerateSwitchStatement(switchStmt);
                break;

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

            case ForInStatementNode forIn:
            {
                GenerateForInStatement(forIn);
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

    // for item in array: a hidden usize index runs 0..N; item is a copy of array[index].
    private void GenerateForInStatement(ForInStatementNode forIn)
    {
        var arrayTypeName = _semanticModel.GetExpressionType(forIn.Iterable);
        var arrayType = ResolveType(arrayTypeName);
        var elementTypeName = _semanticModel.GetType(forIn);
        var elementType = ResolveType(elementTypeName);
        var length = arrayType.ArrayLength;

        // The iterable is evaluated once, before the loop.
        LLVMValueRef arrayPtr;
        switch (forIn.Iterable)
        {
            case IdentifierNode or MemberAccessNode or IndexExpressionNode or DereferenceExpressionNode:
                arrayPtr = ResolveTargetAddress(forIn.Iterable).Pointer;
                break;
            case ArrayLiteralNode literal:
                arrayPtr = _builder.BuildAlloca(arrayType, "for.in.array");
                EmitArrayLiteralElements(literal, arrayPtr, arrayType);
                break;
            default:
                arrayPtr = _builder.BuildAlloca(arrayType, "for.in.array");
                _builder.BuildStore(GenerateExpression(forIn.Iterable), arrayPtr);
                break;
        }

        var indexPtr = _builder.BuildAlloca(_usizeType, "for.in.index");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_usizeType, 0), indexPtr);

        var function = _builder.InsertBlock.Parent;
        var conditionBlock = _context.AppendBasicBlock(function, "for.cond");
        var bodyBlock = _context.AppendBasicBlock(function, "for.body");
        var latchBlock = _context.AppendBasicBlock(function, "for.latch");
        var endBlock = _context.AppendBasicBlock(function, "for.end");

        _builder.BuildBr(conditionBlock);

        _builder.PositionAtEnd(conditionBlock);
        var index = _builder.BuildLoad2(_usizeType, indexPtr, "for.in.i");
        var inRange = _builder.BuildICmp(
            LLVMIntPredicate.LLVMIntULT,
            index,
            LLVMValueRef.CreateConstInt(_usizeType, length),
            "for.in.cond");
        _builder.BuildCondBr(inRange, bodyBlock, endBlock);

        _currentScope = _currentScope.EnterScope();
        _loopTargets.Push(new LoopTargets(endBlock, latchBlock));

        try
        {
            _builder.PositionAtEnd(bodyBlock);

            var elementPtr = _builder.BuildGEP2(
                arrayType,
                arrayPtr,
                new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, 0), index },
                "for.in.element");

            var variable = _builder.BuildAlloca(elementType, forIn.VariableName);
            _builder.BuildStore(_builder.BuildLoad2(elementType, elementPtr, "for.in.item"), variable);
            _currentScope.Declare(
                forIn.VariableName,
                new VariableSymbol(elementTypeName, elementType, variable));

            GenerateScopedStatements(forIn.Body);

            if (!HasTerminator(_builder.InsertBlock))
            {
                _builder.BuildBr(latchBlock);
            }

            _builder.PositionAtEnd(latchBlock);
            var next = _builder.BuildAdd(
                _builder.BuildLoad2(_usizeType, indexPtr, "for.in.i"),
                LLVMValueRef.CreateConstInt(_usizeType, 1),
                "for.in.next");
            _builder.BuildStore(next, indexPtr);
            _builder.BuildBr(conditionBlock);
        }
        finally
        {
            _loopTargets.Pop();
            _currentScope = _currentScope.ExitScope();
        }

        _builder.PositionAtEnd(endBlock);
    }

    // The subject is evaluated once; arms never fall through, each one ends by branching to switch.end
    private void GenerateSwitchStatement(SwitchStatementNode switchStmt)
    {
        var function = _builder.InsertBlock.Parent;
        var subjectTypeName = _semanticModel.GetExpressionType(switchStmt.Subject);
        if (_enumLayouts.ContainsKey(subjectTypeName))
        {
            GenerateEnumSwitch(switchStmt, subjectTypeName);
            return;
        }

        var subject = GenerateExpression(switchStmt.Subject);
        var subjectType = subject.TypeOf;

        var armBlocks = switchStmt.Arms
            .Select((_, i) => _context.AppendBasicBlock(function, $"switch.arm.{i}"))
            .ToList();

        var defaultBlock = switchStmt.DefaultBody is not null
            ? _context.AppendBasicBlock(function, "switch.default")
            : default;
        var endBlock = _context.AppendBasicBlock(function, "switch.end");

        var switchInst = _builder.BuildSwitch(
            subject,
            switchStmt.DefaultBody is not null ? defaultBlock : endBlock,
            (uint)switchStmt.Arms.Sum(arm => arm.Patterns.Count));

        for (var i = 0; i < switchStmt.Arms.Count; i++)
        {
            foreach (var pattern in switchStmt.Arms[i].Patterns)
            {
                var value = CastInitializer(
                    GenerateExpression(pattern),
                    pattern,
                    subjectType,
                    subjectTypeName,
                    "switch_case");

                switchInst.AddCase(value, armBlocks[i]);
            }
        }

        // `break` leaves the switch; `continue` still targets the enclosing loop.
        var continueTarget = _loopTargets.Count > 0 ? _loopTargets.Peek().ContinueTarget : endBlock;
        _loopTargets.Push(new LoopTargets(endBlock, continueTarget));

        try
        {
            for (var i = 0; i < switchStmt.Arms.Count; i++)
            {
                _builder.PositionAtEnd(armBlocks[i]);
                GenerateScopedStatements(switchStmt.Arms[i].Body);

                if (_builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    _builder.BuildBr(endBlock);
                }
            }

            if (switchStmt.DefaultBody is not null)
            {
                _builder.PositionAtEnd(defaultBlock);
                GenerateScopedStatements(switchStmt.DefaultBody);

                if (_builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    _builder.BuildBr(endBlock);
                }
            }
        }
        finally
        {
            _loopTargets.Pop();
        }

        _builder.PositionAtEnd(endBlock);
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
}
