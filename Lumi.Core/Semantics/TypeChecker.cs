using System.Globalization;
using System.Numerics;
using Lumi.Core.Ast;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker
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
            scope.Declare(parameter.Name, parameter.Type);
        }

        foreach (var statement in func.Body)
        {
            CheckStatement(statement, scope, func.ReturnType);
        }

        if (func.ReturnType != TypeConst.Void &&
            !_returnAnalyzer.AlwaysReturns(func.Body))
        {
            throw new Exception(
                $"Function '{func.Name}' does not return a value on every execution path.");
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

            Functions.Register(signature);
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
                    throw new Exception(
                        $"Type Mismatch: Cannot assign a negative value to unsigned type '{varDecl.Type}'.");
                }

                if (!CanAssign(varDecl.Type, initializerType, varDecl.Initializer))
                {
                    throw new Exception(
                        $"Type Mismatch: Cannot assign type '{initializerType}' " +
                        $"to a variable of type '{varDecl.Type}'");
                }

                scope.Declare(varDecl.Name, varDecl.Type);
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
                        throw new Exception(
                            $"Function returning '{expectedReturnType}' must return a value.");
                    }

                    break;
                }

                if (expectedReturnType == TypeConst.Void)
                {
                    throw new Exception(
                        "Void functions cannot return a value.");
                }

                var expressionType = CheckExpression(
                    returnStatement.Expression,
                    scope,
                    expectedReturnType);

                if (!CanAssign(
                        expectedReturnType,
                        expressionType,
                        returnStatement.Expression))
                {
                    throw new Exception(
                        $"Return type mismatch: expected '{expectedReturnType}', " +
                        $"but got '{expressionType}'.");
                }

                break;
            }

            case PrintStatementNode printStmt:
                CheckExpression(printStmt.Expression, scope);
                break;

            case IfStatementNode ifStatement:
            {
                var conditionType = CheckExpression(ifStatement.Condition, scope);

                if (conditionType != TypeConst.Bool)
                {
                    throw new Exception(
                        $"Type Mismatch: An if condition must be 'bool', but got '{conditionType}'.");
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

                if (conditionType != TypeConst.Bool)
                {
                    throw new Exception(
                        $"Type Mismatch: A while condition must be 'bool', but got '{conditionType}'.");
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
            case BreakStatementNode:
                if (_loopDepth == 0)
                {
                    throw new Exception("'break' can only be used inside a loop.");
                }

                break;

            case ContinueStatementNode:
                if (_loopDepth == 0)
                {
                    throw new Exception("'continue' can only be used inside a loop.");
                }

                break;
            case AssignmentStatementNode assignment:
            {
                var variableType = scope.Lookup(assignment.Name);
                var valueType = CheckExpression(assignment.Value, scope, variableType);

                if (!CanAssign(variableType, valueType, assignment.Value))
                {
                    throw new Exception(
                        $"Type Mismatch: Cannot assign '{valueType}' to variable " +
                        $"'{assignment.Name}' of type '{variableType}'.");
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

                    if (conditionType != TypeConst.Bool)
                    {
                        throw new Exception(
                            $"Type Mismatch: A for condition must be 'bool', but got '{conditionType}'.");
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

            IdentifierNode id =>
                scope.Lookup(id.Name),

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

    private string CheckFunctionCall(
        FunctionCallExpressionNode functionCall,
        SymbolTable scope)
    {
        var signature = Functions.Get(functionCall.Name);

        if (functionCall.Arguments.Count != signature.ParameterTypes.Count)
        {
            throw new Exception(
                $"Function '{functionCall.Name}' expects " +
                $"{signature.ParameterTypes.Count} arguments, " +
                $"but received {functionCall.Arguments.Count}.");
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
                throw new Exception(
                    $"Type Mismatch: Argument {i + 1} of function " +
                    $"'{functionCall.Name}' expects '{expectedType}', " +
                    $"but got '{actualType}'.");
            }
        }

        return signature.ReturnType;
    }

    private static void EnsureValueExpression(
        ExpressionNode expression,
        string expressionType)
    {
        if (expressionType == TypeConst.Void)
        {
            throw new Exception(
                "Semantic Error: A void expression cannot be used where a value is required.");
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

            TypeConst.Minus => throw new Exception(
                $"Type Mismatch: Unary '-' cannot be applied to type '{operandType}'."),

            "!" when operandType == TypeConst.Bool => TypeConst.Bool,

            "!" => throw new Exception(
                $"Type Error: Logical NOT requires 'bool', but got '{operandType}'."),

            "~" when TypePromoter.IsIntegerType(operandType) => operandType,

            "~" => throw new Exception(
                $"Type Error: Bitwise NOT requires an integer operand, but got '{operandType}'."),

            _ => throw new Exception(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
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

        if (IsBitwiseOperator(binary.Operator))
        {
            if (!TypePromoter.IsIntegerType(leftType)
                || !TypePromoter.IsIntegerType(rightType))
            {
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires integer operands, got '{leftType}' and '{rightType}'.");
            }

            var commonType = TypePromoter.GetCommonType(leftType, rightType);

            if (commonType is null)
            {
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot be applied to types '{leftType}' and '{rightType}'.");
            }

            return commonType;
        }

        if (IsShiftOperator(binary.Operator))
        {
            if (!TypePromoter.IsIntegerType(leftType)
                || !TypePromoter.IsIntegerType(rightType))
            {
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires integer operands, got '{leftType}' and '{rightType}'.");
            }

            if (TryGetIntegerConstant(binary.Right, out var shiftAmount))
            {
                if (shiftAmount < BigInteger.Zero)
                {
                    throw new Exception(
                        $"Invalid shift amount: '{shiftAmount}'. " +
                        "Shift amounts cannot be negative.");
                }

                var bitWidth = GetIntegerBitWidth(leftType);

                if (shiftAmount >= bitWidth)
                {
                    throw new Exception(
                        $"Invalid shift amount: '{shiftAmount}'. " +
                        $"Type '{leftType}' has a bit width of {bitWidth}, " +
                        $"so the shift amount must be between 0 and {bitWidth - 1}.");
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
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires boolean operands, got '{leftType}' and '{rightType}'.");
            }

            return TypeConst.Bool;
        }

        throw new Exception(
            $"Unsupported binary operator: '{binary.Operator}'.");
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
        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            throw new Exception(
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"requires numeric operands, got '{leftType}' and '{rightType}'.");
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
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot be applied to types '{leftType}' and '{rightType}'.");
            }

            return leftType;
        }

        var commonType = TypePromoter.GetCommonType(leftType, rightType);

        if (commonType is null)
        {
            throw new Exception(
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"cannot be applied to types '{leftType}' and '{rightType}'.");
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

        // Integer operands may have different widths if they can be promoted.
        if (TypePromoter.IsIntegerType(leftType) &&
            TypePromoter.IsIntegerType(rightType))
        {
            EnsureCompatibleNumericTypes(binary, leftType, rightType);
            return TypeConst.Bool;
        }

        // Floating-point comparisons currently require identical types.
        if (IsFloatingPointType(leftType) &&
            IsFloatingPointType(rightType))
        {
            if (leftType != rightType)
            {
                throw CreateComparisonTypeMismatch(binary, leftType, rightType);
            }

            return TypeConst.Bool;
        }

        if (leftType != rightType)
        {
            throw CreateComparisonTypeMismatch(binary, leftType, rightType);
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
            throw new Exception(
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"does not support ordering comparisons on '{leftType}'.");
        }

        throw new Exception(
            $"Type Mismatch: Operator '{binary.Operator}' " +
            $"cannot compare values of type '{leftType}'.");
    }

    private void EnsureCompatibleNumericTypes(
        BinaryExpressionNode binary,
        string leftType,
        string rightType)
    {
        if (TypePromoter.GetCommonType(leftType, rightType) is null)
        {
            throw CreateComparisonTypeMismatch(binary, leftType, rightType);
        }
    }

    private static Exception CreateComparisonTypeMismatch(
        BinaryExpressionNode binary,
        string leftType,
        string rightType)
    {
        return new Exception(
            $"Type Mismatch: Operator '{binary.Operator}' " +
            $"cannot compare types '{leftType}' and '{rightType}'.");
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
}
