using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.CodeGenerator;

public sealed class Codegen
{
    private readonly LLVMBuilderRef _builder;
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly SemanticModel _semanticModel;
    private readonly Dictionary<string, VariableSymbol> _variables = new();

    // Track the return type of the function currently being generated
    private LLVMTypeRef _currentReturnType;

    public Codegen(string moduleName, SemanticModel semanticModel)
    {
        _context = LLVMContextRef.Create();
        _module = _context.CreateModuleWithName(moduleName);
        _builder = _context.CreateBuilder();
        _semanticModel = semanticModel;

        LLVMSharp.Interop.LLVM.InitializeNativeTarget();
        LLVMSharp.Interop.LLVM.InitializeNativeAsmPrinter();
        LLVMSharp.Interop.LLVM.InitializeNativeAsmParser();
    }

    public void GenerateFunction(FunctionDeclarationNode funcNode)
    {
        Console.WriteLine("[DEBUG] GenerateFunction UPDATED VERSION");

        _currentReturnType = ResolveType(funcNode.ReturnType);

        var functionType = LLVMTypeRef.CreateFunction(_currentReturnType, []);
        var function = _module.AddFunction(funcNode.Name, functionType);

        var entryBlock = _context.AppendBasicBlock(function, "entry");
        _builder.PositionAtEnd(entryBlock);

        _variables.Clear();

        GenerateStatements(funcNode.Body);

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

        _module.Verify(LLVMVerifierFailureAction.LLVMPrintMessageAction);
    }

    private void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case VariableDeclarationNode varDecl:
            {
                // 1. Resolve the declared type.
                var declaredType = ResolveType(varDecl.Type);

                // 2. Generate the initializer.
                var initializer = GenerateExpression(varDecl.Initializer);

                // 3. Convert it to the declared type before storing.
                initializer = CastInitializer(
                    initializer,
                    varDecl.Initializer,
                    declaredType,
                    varDecl.Type,
                    $"init_{varDecl.Name}");

                // Allocate storage and store the converted value.
                var alloca = _builder.BuildAlloca(
                    declaredType,
                    varDecl.Name);

                _builder.BuildStore(initializer, alloca);

                _variables[varDecl.Name] = new VariableSymbol(
                    varDecl.Type,
                    declaredType,
                    alloca);

                break;
            }

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
                GenerateIfStatement(ifStmt);
                break;
        }
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

        // Branch from the current condition block.
        _builder.BuildCondBr(
            condition,
            thenBlock,
            statement.ElseBody is not null
                ? elseBlock
                : mergeBlock);

        // Generate the then branch.
        _builder.PositionAtEnd(thenBlock);
        GenerateStatements(statement.ThenBody);

        var thenEndBlock = _builder.InsertBlock;
        var thenCanContinue = !HasTerminator(thenEndBlock);

        if (thenCanContinue)
        {
            _builder.BuildBr(mergeBlock);
        }

        // Generate the else branch, if present.
        var elseCanContinue = statement.ElseBody is null;

        if (statement.ElseBody is not null)
        {
            _builder.PositionAtEnd(elseBlock);
            GenerateStatements(statement.ElseBody);

            var elseEndBlock = _builder.InsertBlock;
            elseCanContinue = !HasTerminator(elseEndBlock);

            if (elseCanContinue)
            {
                _builder.BuildBr(mergeBlock);
            }
        }

        // A merge block is reachable if at least one branch reaches it.
        _builder.PositionAtEnd(mergeBlock);

        if (!thenCanContinue && !elseCanContinue)
        {
            _builder.BuildUnreachable();
        }
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
            LiteralNode<string> num => LLVMValueRef.CreateConstInt(NumberConverterUtils.TranslateIntLiteral(num),
                ulong.Parse(num.Value)),
            LiteralNode<double> floating => LLVMValueRef.CreateConstReal(
                ResolveType(_semanticModel.GetExpressionType(expression)),
                floating.Value),
            IdentifierNode id => LoadVariable(id.Name),
            BinaryExpressionNode binary => GenerateBinary(binary),
            UnaryExpressionNode unary => GenerateUnary(unary),
            LiteralNode<char> character => LLVMValueRef.CreateConstInt(_context.Int8Type, character.Value),
            LiteralNode<bool> boolean => LLVMValueRef.CreateConstInt(_context.Int1Type, (ulong)(boolean.Value ? 1 : 0)),
            _ => throw new Exception("Unknown IR generation node target.")
        };
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

            "-" => throw new NotSupportedException(
                "Unary '-' requires a numeric operand."),

            _ => throw new NotSupportedException(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
    }

    private LLVMValueRef LoadVariable(string name)
    {
        if (_variables.TryGetValue(name, out var variable))
        {
            return _builder.BuildLoad2(variable.LLVMType, variable.Allocation, name);
        }

        throw new Exception(
            $"Compiler Error: Undefined identifier '{name}' reached backend.");
    }

    private LLVMValueRef GenerateBinary(BinaryExpressionNode binary)
    {
        var left = GenerateExpression(binary.Left);
        var right = GenerateExpression(binary.Right);

        if (left.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            || right.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
        {
            throw new NotSupportedException(
                "Pointer arithmetic is not supported.");
        }

        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        // Floating-point operations and comparisons.
        if (left.TypeOf.Kind is
            LLVMTypeKind.LLVMHalfTypeKind
            or LLVMTypeKind.LLVMFloatTypeKind
            or LLVMTypeKind.LLVMDoubleTypeKind)
        {
            if (left.TypeOf != right.TypeOf)
            {
                throw new InvalidOperationException(
                    $"Codegen received incompatible floating-point operand types: " +
                    $"'{leftType}' and '{rightType}'.");
            }

            if (binary.Operator is
                TypeConst.EqualsEquals
                or TypeConst.BangEquals
                or TypeConst.LessThan
                or TypeConst.LessThanOrEquals
                or TypeConst.GreaterThan
                or TypeConst.GreaterThanOrEquals)
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

            if (leftType != rightType)
            {
                throw new InvalidOperationException(
                    $"Codegen received incompatible operand types: " +
                    $"'{leftType}' and '{rightType}'.");
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

        // Integer-only operations and comparisons.
        if (left.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind
            || right.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            throw new NotSupportedException(
                $"Binary operator '{binary.Operator}' requires numeric operands.");
        }

        // Comparisons produce an LLVM i1 value, representing Lumi's bool.
        if (binary.Operator is
            TypeConst.EqualsEquals
            or TypeConst.BangEquals
            or TypeConst.LessThan
            or TypeConst.LessThanOrEquals
            or TypeConst.GreaterThan
            or TypeConst.GreaterThanOrEquals)
        {
            if (leftType != rightType)
            {
                throw new InvalidOperationException(
                    $"Codegen received incompatible operand types: " +
                    $"'{leftType}' and '{rightType}'.");
            }

            var predicate = binary.Operator switch
            {
                TypeConst.EqualsEquals =>
                    LLVMIntPredicate.LLVMIntEQ,

                TypeConst.BangEquals =>
                    LLVMIntPredicate.LLVMIntNE,

                TypeConst.LessThan => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntULT
                    : LLVMIntPredicate.LLVMIntSLT,

                TypeConst.LessThanOrEquals => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntULE
                    : LLVMIntPredicate.LLVMIntSLE,

                TypeConst.GreaterThan => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntUGT
                    : LLVMIntPredicate.LLVMIntSGT,

                TypeConst.GreaterThanOrEquals => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntUGE
                    : LLVMIntPredicate.LLVMIntSGE,

                _ => throw new NotSupportedException(
                    $"Unsupported comparison operator: {binary.Operator}")
            };

            return _builder.BuildICmp(
                predicate,
                left,
                right,
                "cmptmp");
        }

        // Integer arithmetic requires matching semantic types.
        if (leftType != rightType)
        {
            throw new InvalidOperationException(
                $"Codegen received incompatible operand types: " +
                $"'{leftType}' and '{rightType}'.");
        }

        var commonWidth = Math.Max(
            left.TypeOf.IntWidth,
            right.TypeOf.IntWidth);

        var commonType = commonWidth switch
        {
            8 => _context.Int8Type,
            16 => _context.Int16Type,
            32 => _context.Int32Type,
            64 => _context.Int64Type,

            _ => throw new NotSupportedException(
                $"Unsupported arithmetic integer width: {commonWidth}.")
        };

        left = CastInteger(left, commonType, "left_operand_cast");
        right = CastInteger(right, commonType, "right_operand_cast");

        return binary.Operator switch
        {
            TypeConst.Plus =>
                _builder.BuildAdd(left, right, "addtmp"),

            TypeConst.Minus =>
                _builder.BuildSub(left, right, "subtmp"),

            TypeConst.Star =>
                _builder.BuildMul(left, right, "multmp"),

            TypeConst.Slash => IsUnsignedType(leftType)
                ? _builder.BuildUDiv(left, right, "divtmp")
                : _builder.BuildSDiv(left, right, "divtmp"),

            _ => throw new NotSupportedException(
                $"Unsupported IR binary operator: {binary.Operator}")
        };
    }

    private static bool IsUnsignedType(string type)
    {
        return type.StartsWith(
            "u",
            StringComparison.Ordinal);
    }


    private LLVMTypeRef ResolveType(string typeName)
    {
        return typeName switch
        {
            TypeConst.I8 or TypeConst.U8 => _context.Int8Type,
            TypeConst.U16 => _context.Int16Type,
            TypeConst.I32 or TypeConst.U32 => _context.Int32Type,
            TypeConst.I64 or TypeConst.U64 => _context.Int64Type,
            TypeConst.Char => _context.Int8Type,
            TypeConst.Void => _context.VoidType,
            TypeConst.Bool => _context.Int1Type,

            TypeConst.F16 => _context.HalfType,
            TypeConst.F32 => _context.FloatType,
            TypeConst.F64 => _context.DoubleType,

            _ => throw new Exception(
                $"Unsupported backend data primitive type: {typeName}")
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
        if (expression is IdentifierNode identifier && _variables.TryGetValue(identifier.Name, out var variable))
        {
            return variable.IsUnsignedIntegerType()
                ? _builder.BuildZExt(value, targetType, name)
                : _builder.BuildSExt(value, targetType, name);
        }

        // Fallback for integer expressions.
        return _builder.BuildSExt(value, targetType, name);
    }
}
