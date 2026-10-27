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
        _currentReturnType = ResolveType(funcNode.ReturnType);
        var functionType = LLVMTypeRef.CreateFunction(_currentReturnType, []);
        var function = _module.AddFunction(funcNode.Name, functionType);

        var entryBlock = _context.AppendBasicBlock(function, "entry");
        _builder.PositionAtEnd(entryBlock);

        // Clear local scope variables for the new function
        _variables.Clear();

        // Track if a return statement was generated
        var hasReturned = false;

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
                        returnValue = _builder.BuildSExt(returnValue, _currentReturnType, "extended_ret");
                    else if (_currentReturnType.IntWidth < returnValue.TypeOf.IntWidth)
                        returnValue = _builder.BuildTrunc(returnValue, _currentReturnType, "truncated_ret");
                }

                _builder.BuildRet(returnValue);
                break;

            case PrintStatementNode printStmt:
                var printValue = GenerateExpression(printStmt.Expression);
                var expressionType = _semanticModel.GetExpressionType(printStmt.Expression);

                switch (expressionType)
                {
                    case TypeUtils.Char:
                        WriterUtils.EmitPrintChar(printValue, _module, _context, _builder);
                        break;
                    case "i8" or "i16" or "i32" or "i64"
                        or "u8" or "u16" or "u32" or "u64":
                        WriterUtils.EmitPrintInt(printValue, _module, _context, _builder);
                        break;
                    case TypeUtils.Bool:
                        WriterUtils.EmitPrintBool(printValue, _module, _context, _builder);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Printing values of type '{expressionType}' is not supported.");
                }

                if (printStmt.AppendNewLine)
                    WriterUtils.EmitSystemWrite(
                        "\n", _module, _context, _builder);

                break;
        }
    }

    private LLVMValueRef GenerateExpression(ExpressionNode expression)
    {
        return expression switch
        {
            LiteralNode<string> num => LLVMValueRef.CreateConstInt(NumberConverterUtils.TranslateIntLiteral(num),
                ulong.Parse(num.Value)),
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
            "-" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind
                => _builder.BuildNeg(operand, "negtmp"),

            "-" => throw new NotSupportedException(
                "Unary '-' currently supports integer operands only."),

            _ => throw new NotSupportedException(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
    }


    private LLVMValueRef LoadVariable(string name)
    {
        if (_variables.TryGetValue(name, out var variable))
            return _builder.BuildLoad2(variable.LLVMType, variable.Allocation, name);

        throw new Exception(
            $"Compiler Error: Undefined identifier '{name}' reached backend.");
    }

    private LLVMValueRef GenerateBinary(BinaryExpressionNode binary)
    {
        var left = GenerateExpression(binary.Left);
        var right = GenerateExpression(binary.Right);

        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        if (left.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            right.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
            throw new NotSupportedException(
                $"Binary operator '{binary.Operator}' requires integer operands.");

        // Comparisons produce an LLVM i1 value, which represents Lumi's bool.
        if (binary.Operator is
            TypeUtils.EqualsEquals or
            TypeUtils.BangEquals or
            TypeUtils.LessThan or
            TypeUtils.LessThanOrEquals or
            TypeUtils.GreaterThan or
            TypeUtils.GreaterThanOrEquals)
        {
            var predicate = binary.Operator switch
            {
                TypeUtils.EqualsEquals => LLVMIntPredicate.LLVMIntEQ,
                TypeUtils.BangEquals => LLVMIntPredicate.LLVMIntNE,

                TypeUtils.LessThan => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntULT
                    : LLVMIntPredicate.LLVMIntSLT,

                TypeUtils.LessThanOrEquals => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntULE
                    : LLVMIntPredicate.LLVMIntSLE,

                TypeUtils.GreaterThan => IsUnsignedType(leftType)
                    ? LLVMIntPredicate.LLVMIntUGT
                    : LLVMIntPredicate.LLVMIntSGT,

                TypeUtils.GreaterThanOrEquals => IsUnsignedType(leftType)
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

        // Arithmetic requires matching semantic types.
        if (leftType != rightType)
            throw new InvalidOperationException(
                $"Codegen received incompatible operand types: " +
                $"'{leftType}' and '{rightType}'.");

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
            TypeUtils.Plus => _builder.BuildAdd(left, right, "addtmp"),
            TypeUtils.Minus => _builder.BuildSub(left, right, "subtmp"),
            TypeUtils.Star => _builder.BuildMul(left, right, "multmp"),

            TypeUtils.Slash => IsUnsignedType(leftType)
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
            "i8" or "u8" => _context.Int8Type,
            "i16" or "u16" => _context.Int16Type,
            "i32" or "u32" => _context.Int32Type,
            "i64" or "u64" => _context.Int64Type,
            "char" => _context.Int8Type,
            "void" => _context.VoidType,
            TypeUtils.Bool => _context.Int1Type,

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
                return value;

            throw new NotSupportedException(
                $"Cannot convert {sourceType} to {targetType}.");
        }

        if (sourceType.IntWidth == targetType.IntWidth)
            return value;

        if (sourceType.IntWidth < targetType.IntWidth)
            return _builder.BuildSExt(
                value,
                targetType,
                name);

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
                return value;

            throw new NotSupportedException(
                $"Cannot convert initializer to '{targetTypeName}'.");
        }

        var sourceWidth = sourceType.IntWidth;
        var targetWidth = targetType.IntWidth;

        if (sourceWidth == targetWidth)
            return value;

        if (sourceWidth > targetWidth)
            return _builder.BuildTrunc(value, targetType, name);

        // Integer literals currently represent non-negative values.
        // Their magnitude is preserved by zero extension.
        if (expression is LiteralNode<string>)
            return _builder.BuildZExt(value, targetType, name);

        // For variables, preserve their declared signedness.
        if (expression is IdentifierNode identifier && _variables.TryGetValue(identifier.Name, out var variable))
            return variable.IsUnsignedIntegerType()
                ? _builder.BuildZExt(value, targetType, name)
                : _builder.BuildSExt(value, targetType, name);

        // Fallback for integer expressions.
        return _builder.BuildSExt(value, targetType, name);
    }
}