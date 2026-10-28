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

        foreach (var statement in func.Body) CheckStatement(statement, functionScope, func.ReturnType);
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
                    throw new Exception(
                        $"Type Mismatch: Cannot assign a negative value to unsigned type '{varDecl.Type}'.");

                if (!CanAssign(varDecl.Type, initializerType, varDecl.Initializer))
                    throw new Exception(
                        $"Type Mismatch: Cannot assign type '{initializerType}' " +
                        $"to a variable of type '{varDecl.Type}'");

                scope.Declare(varDecl.Name, varDecl.Type);
                break;

            case ExpressionStatementNode exprStmt:
                CheckExpression(exprStmt.Expression, scope);
                break;
            case ReturnStatementNode retStmt:
                var actualReturnType = CheckExpression(retStmt.Value, scope);

                if (actualReturnType != expectedReturnType)
                    throw new Exception(
                        $"Type Mismatch: Function expects to return '{expectedReturnType}' but got '{actualReturnType}' instead.");
                break;
            case PrintStatementNode printStmt:
                CheckExpression(printStmt.Expression, scope);
                break;
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
                InferBoundsUtils.InferNumericBounds(node),

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
        var rightType = CheckExpression(binary.Right, scope, expectedType);

        // Arithmetic operators require matching numeric types.
        if (binary.Operator is TypeConst.Plus
            or TypeConst.Minus
            or TypeConst.Star
            or TypeConst.Slash)
        {
            if (leftType != rightType)
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot be applied to types '{leftType}' and '{rightType}'.");

            if (!IsIntegerType(leftType) && !IsFloatingPointType(leftType))
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"requires numeric operands, got '{leftType}'.");

            return leftType;
        }

        // Comparison operators produce bool.
        if (binary.Operator is TypeConst.EqualsEquals
            or TypeConst.BangEquals
            or TypeConst.LessThan
            or TypeConst.LessThanOrEquals
            or TypeConst.GreaterThan
            or TypeConst.GreaterThanOrEquals)
        {
            if (leftType != rightType)
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot compare types '{leftType}' and '{rightType}'.");

            var isEquality = binary.Operator is "==" or "!=";

            if (!IsIntegerType(leftType)
                && !IsFloatingPointType(leftType)
                && leftType != TypeConst.Char
                && !(leftType == TypeConst.Bool && isEquality))
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    $"cannot compare values of type '{leftType}'.");

            if (leftType == TypeConst.Bool && !isEquality)
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    "does not support boolean operands.");

            if (leftType == TypeConst.Char && !isEquality)
                throw new Exception(
                    $"Type Mismatch: Operator '{binary.Operator}' " +
                    "does not support ordering comparisons on characters.");

            return TypeConst.Bool;
        }

        throw new Exception(
            $"Unsupported binary operator: '{binary.Operator}'.");
    }

    private static bool CanAssign(
        string targetType,
        string sourceType,
        ExpressionNode expression)
    {
        if (targetType == sourceType)
            return true;

        if (expression is LiteralNode<string> integerLiteral
            && IsIntegerType(targetType))
            return FitsInType(integerLiteral.Value, targetType);

        if (expression is LiteralNode<double> floatingLiteral
            && IsFloatingPointType(targetType))
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

        if (targetType == TypeConst.Char
            && expression is LiteralNode<char>)
            return true;

        if (expression is UnaryExpressionNode
            {
                Operator: "-",
                Operand: LiteralNode<string> integerOperand
            }
            && IsSignedIntegerType(targetType))
            return NegativeLiteralFitsInType(
                integerOperand.Value,
                targetType);

        return false;
    }

    private static bool IsIntegerType(string type)
    {
        return type is TypeConst.I16 or TypeConst.I32 or TypeConst.I64
            or TypeConst.U16 or TypeConst.U32 or TypeConst.U64;
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
            return false;

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
            return false;

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