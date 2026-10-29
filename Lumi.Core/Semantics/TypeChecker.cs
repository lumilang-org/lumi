using System.Globalization;
using Lumi.Core.Ast;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker
{
    public SemanticModel SemanticModel { get; } = new();

    public void CheckFunction(FunctionDeclarationNode func)
    {
        // Create a new local scope for the function body
        var functionScope = new SymbolTable();

        foreach (var statement in func.Body)
        {
            CheckStatement(statement, functionScope, func.ReturnType);
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
                var returnType = CheckExpression(
                    returnStatement.Value,
                    scope,
                    expectedReturnType);

                if (returnType != expectedReturnType)
                {
                    throw new Exception(
                        $"Type Mismatch: Function expects to return '{expectedReturnType}' " +
                        $"but got '{returnType}' instead.");
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

            _ => throw new Exception(
                "Unknown expression type encountered during analysis.")
        };

        SemanticModel.SetExpressionType(expression, type);
        return type;
    }

    private string CheckUnaryExpression(
        UnaryExpressionNode unary,
        SymbolTable scope)
    {
        var operandType = CheckExpression(unary.Operand, scope);

        return unary.Operator switch
        {
            "-" when IsSignedIntegerType(operandType)
                     || IsFloatingPointType(operandType)
                => operandType,

            "-" => throw new Exception(
                $"Type Mismatch: Unary '-' cannot be applied to type '{operandType}'."),

            "!" when operandType == TypeConst.Bool => TypeConst.Bool,

            "!" => throw new Exception(
                $"Type Error: Logical NOT requires 'bool', but got '{operandType}'."),

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

        if (IsArithmeticOperator(binary.Operator))
        {
            return CheckArithmeticExpression(
                binary,
                leftType,
                rightType);
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

    private string CheckArithmeticExpression(
        BinaryExpressionNode binary,
        string leftType,
        string rightType)
    {
        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            throw new Exception(
                $"Type Mismatch: Operator '{binary.Operator}' " +
                $"requires numeric operands, got '{leftType}' and '{rightType}'.");
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
}
