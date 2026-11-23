using System.Globalization;
using System.Numerics;
using Lumi.Core.Ast;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker(DiagnosticBag diagnostic)
{
    private readonly Dictionary<string, string> _moduleContexts = new();
    private readonly ReturnAnalyzer _returnAnalyzer = new();
    private readonly Dictionary<string, StructDeclarationNode> _structs = new();
    private IReadOnlyList<ImportDeclarationNode> _currentImports = [];

    private string _currentFile = "<unknown>";
    private string _currentModulePrefix = "";
    private int _loopDepth;
    public SemanticModel SemanticModel { get; } = new();
    private FunctionRegistry Functions => SemanticModel.Functions;

    private void CheckFunction(FunctionDeclarationNode func)
    {
        var scope = new SymbolTable();

        foreach (var parameter in func.Parameters)
        {
            ValidateType(parameter.Type, parameter.Span);
            var resolvedType = ResolveType(parameter.Type);
            SemanticModel.SetType(parameter, resolvedType);
            if (!scope.TryDeclare(parameter.Name, resolvedType))
            {
                Report(
                    DiagnosticCodes.DuplicateVariable,
                    $"Variable '{parameter.Name}' is already declared in this scope.",
                    parameter.Span);
            }
        }

        foreach (var statement in func.Body)
        {
            CheckStatement(statement, scope, func.ReturnType);
        }

        if (func.ReturnType != TypeConst.Void &&
            !_returnAnalyzer.AlwaysReturns(func.Body))
        {
            Report(DiagnosticCodes.MissingReturn,
                $"Function '{func.Name}' does not return a value on every execution path.",
                func.Span);
        }
    }

    public void CheckCompilationUnit(IReadOnlyList<ProgramNode> modules)
    {
        _structs.Clear();
        Functions.Clear(); // Assuming I add a Clear method to FunctionRegistry or handle it here

        // 1. Pass: Register all structs (mangled) across all modules
        foreach (var module in modules)
        {
            var modulePrefix = module.Module != null ? string.Join("::", module.Module.PathSegments) : "";
            _currentFile = module.FileName;

            foreach (var structDecl in module.Structs)
            {
                var mangledName = MangleName(modulePrefix, structDecl.Name);
                if (!_structs.TryAdd(mangledName, structDecl))
                {
                    Report(DiagnosticCodes.TypeMismatch, $"Struct '{mangledName}' is already declared.",
                        structDecl.Span);
                }

                SemanticModel.SetMangledName(structDecl, mangledName);
            }
        }

        // 2. Pass: Register all function signatures (mangled) with resolved types
        foreach (var module in modules)
        {
            var modulePrefix = module.Module != null ? string.Join("::", module.Module.PathSegments) : "";
            _currentFile = module.FileName;
            _currentModulePrefix = modulePrefix;
            _currentImports = module.Imports;

            foreach (var func in module.Functions)
            {
                var mangledName = func.Name == "main" ? "main" : MangleName(modulePrefix, func.Name);
                var signature = new FunctionSignature(
                    mangledName,
                    func.Parameters.Select(p => ResolveType(p.Type)).ToArray(),
                    ResolveType(func.ReturnType));

                if (!Functions.TryRegister(signature))
                {
                    Report(DiagnosticCodes.DuplicateFunction, $"Function '{mangledName}' is already declared.",
                        func.Span);
                }

                SemanticModel.SetType(func, ResolveType(func.ReturnType));
                SemanticModel.SetMangledName(func, mangledName);
            }
        }

        // 3. Pass: Check bodies with cross-module resolution
        foreach (var module in modules)
        {
            var modulePrefix = module.Module != null ? string.Join("::", module.Module.PathSegments) : "";
            _currentFile = module.FileName;
            _currentModulePrefix = modulePrefix;
            _currentImports = module.Imports;

            foreach (var structDecl in module.Structs)
            {
                foreach (var field in structDecl.Fields)
                {
                    ValidateType(field.Type, field.Span);
                }
            }

            foreach (var func in module.Functions)
            {
                CheckFunction(func);
            }
        }
    }

    private string MangleName(string prefix, string name)
    {
        return string.IsNullOrEmpty(prefix) ? name : $"{prefix.Replace("::", "_")}_{name}";
    }

    private string ResolveSymbolName(string name)
    {
        // 1. Try local module
        var localMangled = MangleName(_currentModulePrefix, name);
        if (Functions.Contains(localMangled) || _structs.ContainsKey(localMangled))
        {
            return localMangled;
        }

        // 2. Try imports
        foreach (var import in _currentImports)
        {
            var importPrefix = string.Join("::", import.PathSegments);

            // Handle qualified names like "math::add" or "std::math::add"
            // where "math" or "std::math" is in the imports.
            if (name.Contains("::"))
            {
                var parts = name.Split("::");
                // Check if the prefix of the name matches the import prefix
                // e.g. import math; math::add -> std::math_add
                if (import.PathSegments.SequenceEqual(parts.Take(parts.Length - 1)) ||
                    import.PathSegments.Last() == parts[0])
                {
                    // This is simplified. Let's try to just mangle the whole name and see if it hits.
                    var mangled = name.Replace("::", "_");
                    if (Functions.Contains(mangled) || _structs.ContainsKey(mangled))
                    {
                        return mangled;
                    }

                    // Try adding the full import prefix if parts[0] is just the last segment
                    if (import.PathSegments.Last() == parts[0] && import.PathSegments.Count > 1)
                    {
                        var fullMangled = string.Join("_", import.PathSegments) + "_" + string.Join("_", parts.Skip(1));
                        if (Functions.Contains(fullMangled) || _structs.ContainsKey(fullMangled))
                        {
                            return fullMangled;
                        }
                    }
                }
            }

            var mangledSimple = MangleName(importPrefix, name);
            if (Functions.Contains(mangledSimple) || _structs.ContainsKey(mangledSimple))
            {
                return mangledSimple;
            }
        }

        // 3. Fallback to unmangled (for built-ins or main)
        return name.Replace("::", "_");
    }

    public void CheckProgram(ProgramNode program)
    {
        CheckCompilationUnit([program]);
    }

    private void CheckStatement(StatementNode statement, SymbolTable scope, string expectedReturnType)
    {
        switch (statement)
        {
            case VariableDeclarationNode varDecl:

                ValidateType(varDecl.Type, varDecl.Span);
                var resolvedVarType = ResolveType(varDecl.Type);
                SemanticModel.SetType(varDecl, resolvedVarType);

                if (varDecl.Initializer != null)
                {
                    var initializerType = CheckExpression(
                        varDecl.Initializer,
                        scope,
                        resolvedVarType);

                    EnsureValueExpression(
                        varDecl.Initializer,
                        initializerType);

                    if (resolvedVarType.StartsWith('u') &&
                        varDecl.Initializer is UnaryExpressionNode { Operator: "-" })
                    {
                        Report(DiagnosticCodes.TypeMismatch,
                            $"Cannot assign a negative value to unsigned type '{resolvedVarType}'.",
                            varDecl.Initializer.Span);
                    }

                    if (!CanAssign(resolvedVarType, initializerType, varDecl.Initializer))
                    {
                        Report(DiagnosticCodes.TypeMismatch,
                            $"Cannot assign type '{initializerType}' to a variable of type '{resolvedVarType}'.",
                            varDecl.Initializer.Span);
                    }
                }

                if (!scope.TryDeclare(varDecl.Name, resolvedVarType))
                {
                    Report(
                        DiagnosticCodes.DuplicateVariable,
                        $"Variable '{varDecl.Name}' is already declared in this scope.",
                        varDecl.Span);
                }

                break;

            case ExpressionStatementNode exprStmt:
                CheckExpression(exprStmt.Expression, scope);
                break;

            case ReturnStatementNode returnStatement:
            {
                if (returnStatement.Expression is null)
                {
                    if (expectedReturnType != TypeConst.Void)
                    {
                        Report(DiagnosticCodes.MissingReturn,
                            $"Function returning '{expectedReturnType}' must return a value.",
                            returnStatement.Span);
                    }

                    SemanticModel.SetType(returnStatement, TypeConst.Void);
                    break;
                }

                if (expectedReturnType == TypeConst.Void)
                {
                    Report(DiagnosticCodes.InvalidReturn,
                        "Void functions cannot return a value.",
                        returnStatement.Span);

                    CheckExpression(returnStatement.Expression, scope);
                    SemanticModel.SetType(returnStatement, TypeConst.Void);
                    break;
                }

                var expressionType = CheckExpression(
                    returnStatement.Expression,
                    scope,
                    expectedReturnType);

                EnsureValueExpression(returnStatement.Expression, expressionType);
                SemanticModel.SetType(returnStatement, expressionType);

                if (expressionType != TypeConst.Error &&
                    !CanAssign(
                        expectedReturnType,
                        expressionType,
                        returnStatement.Expression))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Return type mismatch: expected '{expectedReturnType}', " +
                        $"but got '{expressionType}'.",
                        returnStatement.Expression.Span);
                }

                break;
            }

            case PrintStatementNode printStmt:
                CheckExpression(printStmt.Expression, scope);
                break;

            case IfStatementNode ifStatement:
            {
                var conditionType = CheckExpression(ifStatement.Condition, scope);

                if (conditionType != TypeConst.Bool && conditionType != TypeConst.Error)
                {
                    Report(DiagnosticCodes.InvalidCondition,
                        $"Type Mismatch: An if condition must be 'bool', but got '{conditionType}'.",
                        ifStatement.Condition.Span);
                }

                var thenScope = new SymbolTable(scope);

                foreach (var stmt in ifStatement.ThenBody)
                {
                    CheckStatement(stmt, thenScope, expectedReturnType);
                }

                if (ifStatement.ElseBody is not null)
                {
                    var elseScope = new SymbolTable(scope);

                    foreach (var stmt in ifStatement.ElseBody)
                    {
                        CheckStatement(stmt, elseScope, expectedReturnType);
                    }
                }

                break;
            }
            case WhileStatementNode whileStatement:
            {
                var conditionType = CheckExpression(whileStatement.Condition, scope);

                if (conditionType != TypeConst.Bool && conditionType != TypeConst.Error)
                {
                    Report(DiagnosticCodes.InvalidCondition,
                        $"Type Mismatch: A while condition must be 'bool', but got '{conditionType}'.",
                        whileStatement.Condition.Span);
                }

                var bodyScope = new SymbolTable(scope);

                _loopDepth++;

                try
                {
                    foreach (var stmt in whileStatement.Body)
                    {
                        CheckStatement(stmt, bodyScope, expectedReturnType);
                    }
                }
                finally
                {
                    _loopDepth--;
                }

                break;
            }
            case BreakStatementNode breakStmt:
                if (_loopDepth == 0)
                {
                    Report(DiagnosticCodes.BreakOutsideLoop, "'break' can only be used inside a loop.", breakStmt.Span);
                }

                break;

            case ContinueStatementNode continueStatement:
                if (_loopDepth == 0)
                {
                    Report(DiagnosticCodes.ContinueOutsideLoop, "'continue' can only be used inside a loop.",
                        continueStatement.Span);
                }

                break;
            case AssignmentStatementNode assignment:
            {
                if (!scope.TryLookup(assignment.Name, out var variableType))
                {
                    Report(DiagnosticCodes.UndefinedVariable,
                        $"Undefined variable '{assignment.Name}'.",
                        assignment.Span);

                    // Still check the right-hand side for other diagnostics.
                    CheckExpression(assignment.Value, scope);
                    break;
                }

                var valueType = CheckExpression(
                    assignment.Value,
                    scope,
                    variableType);

                EnsureValueExpression(assignment.Value, valueType);

                if (valueType != TypeConst.Error &&
                    !CanAssign(variableType, valueType, assignment.Value))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Cannot assign '{valueType}' to variable " +
                        $"'{assignment.Name}' of type '{variableType}'.",
                        assignment.Value.Span);
                }

                break;
            }

            case TargetAssignmentStatementNode targetAssignment:
            {
                string targetType;

                switch (targetAssignment.Target)
                {
                    case IdentifierNode id:
                        if (!scope.TryLookup(id.Name, out targetType!))
                        {
                            Report(DiagnosticCodes.UndefinedVariable,
                                $"Undefined variable '{id.Name}'.",
                                targetAssignment.Target.Span);
                            CheckExpression(targetAssignment.Value, scope);
                            return;
                        }

                        SemanticModel.SetExpressionType(id, targetType);
                        break;

                    case MemberAccessNode member:
                        targetType = CheckMemberAccess(member, scope);
                        SemanticModel.SetExpressionType(member, targetType);
                        break;

                    case IndexExpressionNode indexExpr:
                        targetType = CheckIndexExpression(indexExpr, scope);
                        SemanticModel.SetExpressionType(indexExpr, targetType);
                        break;

                    default:
                        Report(DiagnosticCodes.TypeMismatch,
                            "Invalid assignment target.",
                            targetAssignment.Target.Span);
                        CheckExpression(targetAssignment.Value, scope);
                        return;
                }

                var valueType = CheckExpression(targetAssignment.Value, scope, targetType);
                EnsureValueExpression(targetAssignment.Value, valueType);

                if (targetType != TypeConst.Error &&
                    valueType != TypeConst.Error &&
                    !CanAssign(targetType, valueType, targetAssignment.Value))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Cannot assign '{valueType}' to target of type '{targetType}'.",
                        targetAssignment.Value.Span);
                }

                break;
            }

            case ForStatementNode forStatement:
            {
                // The initializer variable must only exist within this loop.
                var forScope = new SymbolTable(scope);

                // Initializer
                if (forStatement.Initializer is not null)
                {
                    CheckStatement(
                        forStatement.Initializer,
                        forScope,
                        expectedReturnType);
                }

                // Condition
                if (forStatement.Condition is not null)
                {
                    var conditionType = CheckExpression(
                        forStatement.Condition,
                        forScope);

                    if (conditionType != TypeConst.Bool && conditionType != TypeConst.Error)
                    {
                        Report(DiagnosticCodes.InvalidCondition,
                            $"Type Mismatch: A for condition must be 'bool', but got '{conditionType}'.",
                            forStatement.Condition.Span);
                    }
                }

                // Update and body are inside the loop scope.
                _loopDepth++;

                try
                {
                    if (forStatement.Update is not null)
                    {
                        CheckStatement(
                            forStatement.Update,
                            forScope,
                            expectedReturnType);
                    }

                    var bodyScope = new SymbolTable(forScope);

                    foreach (var stmt in forStatement.Body)
                    {
                        CheckStatement(
                            stmt,
                            bodyScope,
                            expectedReturnType);
                    }
                }
                finally
                {
                    _loopDepth--;
                }

                break;
            }
        }
    }

    private string CheckExpression(
        ExpressionNode expression,
        SymbolTable scope,
        string? expectedType = null)
    {
        var type = expression switch
        {
            LiteralNode<string> node =>
                expectedType is not null
                && TypePromoter.IsIntegerType(expectedType)
                && FitsInType(node.Value, expectedType)
                    ? expectedType
                    : InferBoundsUtils.InferNumericBounds(node),

            LiteralNode<double> node =>
                expectedType is not null && IsFloatingPointType(expectedType)
                    ? expectedType
                    : InferBoundsUtils.InferFloating(node),

            IdentifierNode id => CheckIdentifier(id, scope),

            BinaryExpressionNode binary =>
                CheckBinaryExpression(binary, scope, expectedType),

            UnaryExpressionNode unary =>
                CheckUnaryExpression(unary, scope),

            LiteralNode<char> _ => TypeConst.Char,
            LiteralNode<bool> _ => TypeConst.Bool,
            ArrayLiteralNode arrayLiteral => CheckArrayLiteral(arrayLiteral, scope, expectedType),
            IndexExpressionNode indexExpression => CheckIndexExpression(indexExpression, scope),
            FunctionCallExpressionNode functionCall => CheckFunctionCall(functionCall, scope),
            StructLiteralNode structLiteral => CheckStructLiteral(structLiteral, scope),
            MemberAccessNode memberAccess => CheckMemberAccess(memberAccess, scope),

            _ => throw new Exception(
                "Unknown expression type encountered during analysis.")
        };

        SemanticModel.SetExpressionType(expression, type);
        return type;
    }

    private string CheckStructLiteral(
        StructLiteralNode literal,
        SymbolTable scope)
    {
        var resolvedName = ResolveSymbolName(literal.StructName);
        if (!_structs.TryGetValue(resolvedName, out var structDecl))
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Unknown struct type '{literal.StructName}'.",
                literal.Span);

            foreach (var init in literal.Initializers)
            {
                CheckExpression(init.Value, scope);
            }

            return TypeConst.Error;
        }

        SemanticModel.SetMangledName(literal, resolvedName);
        SemanticModel.SetExpressionType(literal, resolvedName);

        var declaredFields = structDecl.Fields.ToDictionary(f => f.Name, f => f.Type);
        var seenFields = new HashSet<string>();

        foreach (var init in literal.Initializers)
        {
            if (!declaredFields.TryGetValue(init.FieldName, out var fieldType))
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Struct '{literal.StructName}' has no field '{init.FieldName}'.",
                    init.Span);
                CheckExpression(init.Value, scope);
                continue;
            }

            if (!seenFields.Add(init.FieldName))
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Duplicate initializer for field '{init.FieldName}' in '{literal.StructName}'.",
                    init.Span);
            }

            var valueType = CheckExpression(init.Value, scope, fieldType);
            EnsureValueExpression(init.Value, valueType);

            if (valueType != TypeConst.Error &&
                !CanAssign(fieldType, valueType, init.Value))
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Cannot assign type '{valueType}' to field '{init.FieldName}' of type '{fieldType}'.",
                    init.Value.Span);
            }
        }

        foreach (var field in structDecl.Fields)
        {
            if (!seenFields.Contains(field.Name))
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Missing initializer for field '{field.Name}' in struct '{literal.StructName}'.",
                    literal.Span);
            }
        }

        return resolvedName;
    }

    private string CheckMemberAccess(
        MemberAccessNode memberAccess,
        SymbolTable scope)
    {
        var targetType = CheckExpression(memberAccess.Target, scope);

        if (targetType == TypeConst.Error)
        {
            return TypeConst.Error;
        }

        if (IsArrayType(targetType))
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Type '{targetType}' is an array and does not support member access.",
                memberAccess.Span);
            return TypeConst.Error;
        }

        if (!_structs.TryGetValue(targetType, out var structDecl))
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Type '{targetType}' is not a struct and has no member '{memberAccess.MemberName}'.",
                memberAccess.Span);
            return TypeConst.Error;
        }

        var field = structDecl.Fields.FirstOrDefault(f => f.Name == memberAccess.MemberName);
        if (field is null)
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Struct '{targetType}' has no member '{memberAccess.MemberName}'.",
                memberAccess.Span);
            return TypeConst.Error;
        }

        return ResolveType(field.Type);
    }

    private string CheckIdentifier(IdentifierNode identifier, SymbolTable scope)
    {
        if (scope.TryLookup(identifier.Name, out var type))
        {
            return type;
        }

        Report(DiagnosticCodes.UndefinedVariable,
            $"Undefined variable '{identifier.Name}'.",
            identifier.Span);

        return TypeConst.Error;
    }

    private string CheckFunctionCall(
        FunctionCallExpressionNode functionCall,
        SymbolTable scope)
    {
        var resolvedName = ResolveSymbolName(functionCall.Name);
        if (!Functions.TryGet(resolvedName, out var signature))
        {
            Report(DiagnosticCodes.UndefinedFunction,
                $"Function '{functionCall.Name}' is not declared.",
                functionCall.Span);

            // Still inspect the arguments for additional diagnostics.
            foreach (var argument in functionCall.Arguments)
            {
                CheckExpression(argument, scope);
            }

            return TypeConst.Error;
        }

        SemanticModel.SetMangledName(functionCall, resolvedName);

        if (functionCall.Arguments.Count != signature.ParameterTypes.Count)
        {
            var errorMessage = $"Function '{functionCall.Name}' expects " +
                               $"{signature.ParameterTypes.Count} arguments, " +
                               $"but received {functionCall.Arguments.Count}.";

            Report(DiagnosticCodes.ArgumentCountMismatch,
                errorMessage,
                functionCall.Span);
        }

        for (var i = 0; i < functionCall.Arguments.Count; i++)
        {
            var argument = functionCall.Arguments[i];
            var expectedType = signature.ParameterTypes[i];

            var actualType = CheckExpression(
                argument,
                scope,
                expectedType);

            SemanticModel.SetExpressionType(argument, actualType);

            if (!CanAssign(expectedType, actualType, argument))
            {
                var errorMessage = $"Type Mismatch: Argument {i + 1} of function " +
                                   $"'{functionCall.Name}' expects '{expectedType}', " +
                                   $"but got '{actualType}'.";

                Report(DiagnosticCodes.TypeMismatch,
                    errorMessage,
                    functionCall.Span);
            }
        }

        return signature.ReturnType;
    }

    private void EnsureValueExpression(
        ExpressionNode expression,
        string expressionType)
    {
        if (expressionType == TypeConst.Void)
        {
            Report(DiagnosticCodes.VoidExpression,
                "Semantic Error: A void expression cannot be used where a value is required.",
                expression.Span);
        }
    }

    private string CheckUnaryExpression(
        UnaryExpressionNode unary,
        SymbolTable scope)
    {
        var operandType = CheckExpression(unary.Operand, scope);

        return unary.Operator switch
        {
            TypeConst.Minus when IsSignedIntegerType(operandType)
                                 || IsFloatingPointType(operandType)
                => operandType,

            TypeConst.Minus when operandType == TypeConst.Error => TypeConst.Error,

            TypeConst.Minus => ReportErrorAndReturn(
                DiagnosticCodes.TypeMismatch,
                $"Type Mismatch: Unary '-' cannot be applied to type '{operandType}'.",
                unary.Span),

            "!" when operandType == TypeConst.Bool => TypeConst.Bool,

            "!" when operandType == TypeConst.Error => TypeConst.Error,

            "!" => ReportErrorAndReturn(
                DiagnosticCodes.TypeMismatch,
                $"Type Error: Logical NOT requires 'bool', but got '{operandType}'.",
                unary.Span),

            "~" when TypePromoter.IsIntegerType(operandType) => operandType,

            "~" when operandType == TypeConst.Error => TypeConst.Error,

            "~" => ReportErrorAndReturn(
                DiagnosticCodes.TypeMismatch,
                $"Type Error: Bitwise NOT requires an integer operand, but got '{operandType}'.",
                unary.Span),

            _ => ReportErrorAndReturn(
                DiagnosticCodes.InvalidOperator,
                $"Unsupported unary operator: '{unary.Operator}'.",
                unary.Span)
        };
    }

    private string ReportErrorAndReturn(string code, string message, SourceSpan? span)
    {
        Report(code, message, span);
        return TypeConst.Error;
    }

    private static bool IsSignedIntegerType(string type)
    {
        return type is "i8" or "i16" or "i32" or "i64";
    }

    private string CheckBinaryExpression(
        BinaryExpressionNode binary,
        SymbolTable scope,
        string? expectedType = null)
    {
        var leftType = CheckExpression(binary.Left, scope, expectedType);

        var rightExpectedType =
            binary.Right is LiteralNode<string>
            && TypePromoter.IsIntegerType(leftType)
                ? leftType
                : expectedType;

        var rightType = CheckExpression(
            binary.Right,
            scope,
            rightExpectedType);

        EnsureValueExpression(binary.Left, leftType);
        EnsureValueExpression(binary.Right, rightType);

        if (IsArithmeticOperator(binary.Operator))
        {
            return CheckArithmeticExpression(
                binary,
                leftType,
                rightType,
                expectedType);
        }

        if (leftType == TypeConst.Error || rightType == TypeConst.Error)
        {
            return TypeConst.Error;
        }

        if (IsBitwiseOperator(binary.Operator))
        {
            if (!TypePromoter.IsIntegerType(leftType)
                || !TypePromoter.IsIntegerType(rightType))
            {
                return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires integer operands, got '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            var commonType = TypePromoter.GetCommonType(leftType, rightType);

            if (commonType is null)
            {
                return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot be applied to types '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            return commonType;
        }

        if (IsShiftOperator(binary.Operator))
        {
            if (!TypePromoter.IsIntegerType(leftType)
                || !TypePromoter.IsIntegerType(rightType))
            {
                return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires integer operands, got '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            if (TryGetIntegerConstant(binary.Right, out var shiftAmount))
            {
                if (shiftAmount < BigInteger.Zero)
                {
                    Report(DiagnosticCodes.InvalidShiftAmount,
                        $"Invalid shift amount: '{shiftAmount}'. " +
                        "Shift amounts cannot be negative.",
                        binary.Right.Span);
                }

                var bitWidth = GetIntegerBitWidth(leftType);

                if (shiftAmount >= bitWidth)
                {
                    Report(DiagnosticCodes.InvalidShiftAmount,
                        $"Invalid shift amount: '{shiftAmount}'. " +
                        $"Type '{leftType}' has a bit width of {bitWidth}, " +
                        $"so the shift amount must be between 0 and {bitWidth - 1}.",
                        binary.Right.Span);
                }
            }

            return leftType;
        }

        if (IsComparisonOperator(binary.Operator))
        {
            return CheckComparisonExpression(
                binary,
                leftType,
                rightType);
        }

        if (binary.Operator is TypeConst.LogicalAnd or TypeConst.LogicalOr)
        {
            if (leftType != TypeConst.Bool || rightType != TypeConst.Bool)
            {
                return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires boolean operands, got '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            return TypeConst.Bool;
        }

        return ReportErrorAndReturn(DiagnosticCodes.InvalidOperator,
            $"Unsupported binary operator: '{binary.Operator}'.",
            binary.Span);
    }

    public static bool IsShiftOperator(string op)
    {
        return op is TypeConst.ShiftRight or TypeConst.ShiftLeft;
    }

    private static bool IsBitwiseOperator(string op)
    {
        return op is "&" or "|" or "^";
    }

    private static bool TryGetIntegerConstant(
        ExpressionNode expression,
        out BigInteger value)
    {
        switch (expression)
        {
            case LiteralNode<string> literal:
                return BigInteger.TryParse(
                    literal.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value);

            case UnaryExpressionNode
            {
                Operator: "-",
                Operand: LiteralNode<string> literal
            }:
                if (BigInteger.TryParse(
                        literal.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var magnitude))
                {
                    value = -magnitude;
                    return true;
                }

                break;
        }

        value = BigInteger.Zero;
        return false;
    }

    private static int GetIntegerBitWidth(string type)
    {
        return type switch
        {
            TypeConst.I8 or TypeConst.U8 => 8,
            TypeConst.I16 or TypeConst.U16 => 16,
            TypeConst.I32 or TypeConst.U32 => 32,
            TypeConst.I64 or TypeConst.U64 => 64,

            _ => throw new Exception(
                $"Cannot determine integer bit width for type '{type}'.")
        };
    }

    private string CheckArithmeticExpression(
        BinaryExpressionNode binary,
        string leftType,
        string rightType,
        string? expectedType = null)
    {
        if (leftType == TypeConst.Error || rightType == TypeConst.Error)
        {
            return TypeConst.Error;
        }

        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"requires numeric operands, got '{leftType}' and '{rightType}'.",
                binary.Span);
        }

        if (expectedType is not null &&
            TypePromoter.IsIntegerType(expectedType) &&
            leftType == expectedType &&
            rightType == expectedType)
        {
            return expectedType;
        }

        // Floating-point promotion is not handled by TypePromoter yet.
        if (IsFloatingPointType(leftType) || IsFloatingPointType(rightType))
        {
            if (leftType != rightType)
            {
                return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot be applied to types '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            return leftType;
        }

        var commonType = TypePromoter.GetCommonType(leftType, rightType);

        if (commonType is null)
        {
            return ReportErrorAndReturn(DiagnosticCodes.TypeMismatch,
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"cannot be applied to types '{leftType}' and '{rightType}'.",
                binary.Span);
        }

        return commonType;
    }

    private string CheckComparisonExpression(
        BinaryExpressionNode binary,
        string leftType,
        string rightType)
    {
        var isEquality = binary.Operator is
            TypeConst.EqualsEquals or TypeConst.BangEquals;

        if (leftType == TypeConst.Error || rightType == TypeConst.Error)
        {
            return TypeConst.Bool;
        }

        // Integer operands may have different widths if they can be promoted.
        if (TypePromoter.IsIntegerType(leftType) &&
            TypePromoter.IsIntegerType(rightType))
        {
            if (TypePromoter.GetCommonType(leftType, rightType) is null)
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot compare types '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            return TypeConst.Bool;
        }

        // Floating-point comparisons currently require identical types.
        if (IsFloatingPointType(leftType) &&
            IsFloatingPointType(rightType))
        {
            if (leftType != rightType)
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot compare types '{leftType}' and '{rightType}'.",
                    binary.Span);
            }

            return TypeConst.Bool;
        }

        if (leftType != rightType)
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"cannot compare types '{leftType}' and '{rightType}'.",
                binary.Span);
            return TypeConst.Bool;
        }

        if (leftType == TypeConst.Bool && isEquality)
        {
            return TypeConst.Bool;
        }

        if (leftType == TypeConst.Char && isEquality)
        {
            return TypeConst.Bool;
        }

        if (leftType == TypeConst.Char || leftType == TypeConst.Bool)
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"does not support ordering comparisons on '{leftType}'.",
                binary.Span);
            return TypeConst.Bool;
        }

        Report(DiagnosticCodes.TypeMismatch,
            $"Type Mismatch: Operator '{binary.Operator}' " +
            $"cannot compare values of type '{leftType}'.",
            binary.Span);

        return TypeConst.Bool;
    }

    private static bool IsArithmeticOperator(string op)
    {
        return op is TypeConst.Plus
            or TypeConst.Minus
            or TypeConst.Star
            or TypeConst.Slash;
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

    private bool IsNumericType(string type)
    {
        return TypePromoter.IsIntegerType(type)
               || IsFloatingPointType(type);
    }

    private string CheckArrayLiteral(ArrayLiteralNode literal, SymbolTable scope, string? expectedType)
    {
        string? expectedElementType = null;
        var expectedLength = -1;

        if (expectedType != null && IsArrayType(expectedType))
        {
            ParseArrayType(expectedType, out expectedElementType, out expectedLength);
        }

        if (literal.Elements.Count == 0)
        {
            if (expectedElementType == null)
            {
                Report(DiagnosticCodes.TypeMismatch,
                    "Cannot infer type of empty array literal. Please provide an explicit type.", literal.Span);
                return TypeConst.Error;
            }

            return expectedType!;
        }

        var elementType = expectedElementType;
        foreach (var element in literal.Elements)
        {
            var actualElementType = CheckExpression(element, scope, elementType);
            if (elementType == null)
            {
                if (actualElementType != TypeConst.Error)
                {
                    elementType = actualElementType;
                }
            }
            else if (actualElementType != TypeConst.Error && !CanAssign(elementType, actualElementType, element))
            {
                Report(DiagnosticCodes.TypeMismatch,
                    $"Array element type mismatch: expected '{elementType}', but got '{actualElementType}'.",
                    element.Span);
            }
        }

        if (elementType == null || elementType == TypeConst.Error)
        {
            return TypeConst.Error;
        }

        if (expectedLength != -1 && literal.Elements.Count != expectedLength)
        {
            Report(DiagnosticCodes.TypeMismatch,
                $"Array literal length mismatch: expected {expectedLength} elements, but got {literal.Elements.Count}.",
                literal.Span);
        }

        return $"{elementType}[{literal.Elements.Count}]";
    }

    private string CheckIndexExpression(IndexExpressionNode indexExpr, SymbolTable scope)
    {
        var targetType = CheckExpression(indexExpr.Target, scope);
        var indexType = CheckExpression(indexExpr.Index, scope, TypeConst.I32);

        if (targetType == TypeConst.Error || indexType == TypeConst.Error)
        {
            return TypeConst.Error;
        }

        if (!IsArrayType(targetType))
        {
            Report(DiagnosticCodes.TypeMismatch, $"Cannot index into non-array type '{targetType}'.",
                indexExpr.Target.Span);
            return TypeConst.Error;
        }

        if (!TypePromoter.IsIntegerType(indexType))
        {
            Report(DiagnosticCodes.TypeMismatch, $"Array index must be an integer, but got '{indexType}'.",
                indexExpr.Index.Span);
        }

        if (TryGetIntegerConstant(indexExpr.Index, out var indexValue))
        {
            ParseArrayType(targetType, out _, out var length);
            if (indexValue < 0 || indexValue >= length)
            {
                Report(DiagnosticCodes.IndexOutOfBounds,
                    $"Array index '{indexValue}' is out of bounds for array of length {length}.", indexExpr.Index.Span);
            }
        }

        ParseArrayType(targetType, out var elementType, out _);
        return elementType;
    }

    private bool IsArrayType(string type)
    {
        return type.Contains('[') && type.EndsWith(']');
    }

    private void ParseArrayType(string type, out string elementType, out int length)
    {
        var lastBracket = type.LastIndexOf('[');
        elementType = type.Substring(0, lastBracket);
        var lengthStr = type.Substring(lastBracket + 1, type.Length - lastBracket - 2);
        int.TryParse(lengthStr, out length);
    }

    private void ValidateType(string type, SourceSpan? span)
    {
        if (IsArrayType(type))
        {
            ParseArrayType(type, out var elementType, out var length);
            if (length <= 0)
            {
                Report(DiagnosticCodes.InvalidArrayLength,
                    $"Array length must be a positive integer, but got {length}.", span);
            }

            ValidateType(elementType, span);
            return;
        }

        if (TypePromoter.IsIntegerType(type) ||
            IsFloatingPointType(type) ||
            type == TypeConst.Bool ||
            type == TypeConst.Char ||
            type == TypeConst.Void)
        {
            return;
        }

        var resolvedType = ResolveSymbolName(type);
        if (_structs.ContainsKey(resolvedType))
        {
            return;
        }

        Report(DiagnosticCodes.TypeMismatch, $"Unknown type '{type}'.", span);
    }

    private string ResolveType(string type)
    {
        if (IsArrayType(type))
        {
            ParseArrayType(type, out var elementType, out var length);
            return $"{ResolveType(elementType)}[{length}]";
        }

        if (TypePromoter.IsIntegerType(type) ||
            IsFloatingPointType(type) ||
            type == TypeConst.Bool ||
            type == TypeConst.Char ||
            type == TypeConst.Void ||
            type == TypeConst.Error)
        {
            return type;
        }

        return ResolveSymbolName(type);
    }


    private static bool CanAssign(
        string targetType,
        string sourceType,
        ExpressionNode expression)
    {
        if (targetType == sourceType)
        {
            return true;
        }

        if (expression is LiteralNode<string> integerLiteral
            && TypePromoter.IsIntegerType(targetType))
        {
            return FitsInType(integerLiteral.Value, targetType);
        }

        if (expression is LiteralNode<double> floatingLiteral
            && IsFloatingPointType(targetType))
        {
            return double.IsFinite(floatingLiteral.Value)
                   && targetType switch
                   {
                       var t when t == TypeConst.F16 =>
                           Math.Abs(floatingLiteral.Value) <= 65504.0,

                       var t when t == TypeConst.F32 =>
                           Math.Abs(floatingLiteral.Value) <= float.MaxValue,

                       var t when t == TypeConst.F64 => true,

                       _ => false
                   };
        }

        if (targetType == TypeConst.Char
            && expression is LiteralNode<char>)
        {
            return true;
        }

        if (expression is UnaryExpressionNode
            {
                Operator: "-",
                Operand: LiteralNode<string> integerOperand
            }
            && IsSignedIntegerType(targetType))
        {
            return NegativeLiteralFitsInType(
                integerOperand.Value,
                targetType);
        }

        return false;
    }

    private static bool IsFloatingPointType(string type)
    {
        return type is TypeConst.F16 or TypeConst.F32 or TypeConst.F64;
    }

    private static bool FitsInType(string value, string type)
    {
        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unsignedValue))
        {
            return false;
        }

        return type switch
        {
            "u8" => unsignedValue <= byte.MaxValue,
            "u16" => unsignedValue <= ushort.MaxValue,
            "u32" => unsignedValue <= uint.MaxValue,
            "u64" => true,

            "i8" => unsignedValue <= (ulong)sbyte.MaxValue,
            "i16" => unsignedValue <= (ulong)short.MaxValue,
            "i32" => unsignedValue <= int.MaxValue,
            "i64" => unsignedValue <= long.MaxValue,

            _ => false
        };
    }

    private static bool NegativeLiteralFitsInType(string value, string type)
    {
        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var magnitude))
        {
            return false;
        }

        return type switch
        {
            "i8" => magnitude <= 128,
            "i16" => magnitude <= 32768,
            "i32" => magnitude <= 2147483648UL,
            "i64" => magnitude <= 9223372036854775808UL,
            _ => false
        };
    }

    public static bool IsUnsignedInteger(string typeName)
    {
        return typeName.StartsWith('u');
    }

    private void Report(
        string code,
        string message,
        SourceSpan? span)
    {
        diagnostic.Report(new Diagnostic(
            _currentFile,
            code,
            DiagnosticSeverity.Error,
            message,
            span));
    }
}
