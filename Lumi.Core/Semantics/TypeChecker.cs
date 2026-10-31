using System.Globalization;
using System.Numerics;
using Lumi.Core.Ast;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker(DiagnosticBag diagnostic)
{
    private readonly ReturnAnalyzer _returnAnalyzer = new();
    private int _loopDepth;
    public SemanticModel SemanticModel { get; } = new();
    private FunctionRegistry Functions { get; } = new();

    private void CheckFunction(FunctionDeclarationNode func)
    {
        var scope = new SymbolTable();

        foreach (var parameter in func.Parameters)
        {
            if (!scope.TryDeclare(parameter.Name, parameter.Type))
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

    public void CheckProgram(ProgramNode program)
    {
        foreach (var func in program.Functions)
        {
            var signature = new FunctionSignature(
                func.Name,
                func.Parameters.Select(parameter => parameter.Type).ToArray(),
                func.ReturnType);

            if (!Functions.TryRegister(signature))
            {
                Report(DiagnosticCodes.DuplicateFunction,
                    $"Function '{func.Name}' is already declared.",
                    func.Span);
            }
        }

        foreach (var func in program.Functions)
        {
            CheckFunction(func);
        }
    }

    private void CheckStatement(StatementNode statement, SymbolTable scope, string expectedReturnType)
    {
        switch (statement)
        {
            case VariableDeclarationNode varDecl:

                var initializerType = CheckExpression(
                    varDecl.Initializer,
                    scope,
                    varDecl.Type);

                EnsureValueExpression(
                    varDecl.Initializer,
                    initializerType);

                if (varDecl.Type.StartsWith('u') &&
                    varDecl.Initializer is UnaryExpressionNode { Operator: "-" })
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Cannot assign a negative value to unsigned type '{varDecl.Type}'.",
                        varDecl.Initializer.Span);
                }

                if (!CanAssign(varDecl.Type, initializerType, varDecl.Initializer))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Cannot assign type '{initializerType}' to a variable of type '{varDecl.Type}'.",
                        varDecl.Initializer.Span);
                }

                if (!scope.TryDeclare(varDecl.Name, varDecl.Type))
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

                    break;
                }

                if (expectedReturnType == TypeConst.Void)
                {
                    Report(DiagnosticCodes.InvalidReturn,
                        "Void functions cannot return a value.",
                        returnStatement.Span);

                    CheckExpression(returnStatement.Expression, scope);
                    break;
                }

                var expressionType = CheckExpression(
                    returnStatement.Expression,
                    scope,
                    expectedReturnType);

                EnsureValueExpression(returnStatement.Expression, expressionType);

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
            FunctionCallExpressionNode functionCall => CheckFunctionCall(functionCall, scope),

            _ => throw new Exception(
                "Unknown expression type encountered during analysis.")
        };

        SemanticModel.SetExpressionType(expression, type);
        return type;
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
        if (!Functions.TryGet(functionCall.Name, out var signature))
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
            code,
            DiagnosticSeverity.Error,
            message,
            span));
    }
}
