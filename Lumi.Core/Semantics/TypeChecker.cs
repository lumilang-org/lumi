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
                // Check the type of the assignment value
                var initializerType = CheckExpression(varDecl.Initializer, scope);

                if (varDecl.Type.StartsWith('u') &&
                    varDecl.Initializer is UnaryExpressionNode { Operator: "-" })
                    throw new Exception(
                        $"Type Mismatch: Cannot assign a negative value to unsigned type '{varDecl.Type}'.");

                if (!CanAssign(varDecl.Type, initializerType, varDecl.Initializer))
                    throw new Exception(
                        $"Type Mismatch: Cannot assign type '{initializerType}' " +
                        $"to a variable of type '{varDecl.Type}'");

                // Register variable in the current local scope
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

    private string CheckExpression(ExpressionNode expression, SymbolTable scope)
    {
        var type = expression switch
        {
            LiteralNode<string> node => InferBoundsUtils.InferNumericBounds(node),

            IdentifierNode id => scope.Lookup(id.Name),

            BinaryExpressionNode binary => CheckBinaryExpression(binary, scope),

            UnaryExpressionNode unary => CheckUnaryExpression(unary, scope),

            LiteralNode<char> _ => TypeUtils.Char,

            _ => throw new Exception(
                "Unknown expression type encountered during analysis.")
        };

        SemanticModel.SetExpressionType(expression, type);
        return type;
    }

    private string CheckUnaryExpression(UnaryExpressionNode unary, SymbolTable scope)
    {
        var operandType = CheckExpression(unary.Operand, scope);

        return unary.Operator switch
        {
            "-" when operandType is "i8" or "i16" or "i32" or "i64"
                => operandType,

            "-" => throw new Exception(
                $"Type Mismatch: Unary '-' cannot be applied to type '{operandType}'."),

            _ => throw new Exception(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
    }

    private string CheckBinaryExpression(BinaryExpressionNode binary, SymbolTable scope)
    {
        var leftType = CheckExpression(binary.Left, scope);
        var rightType = CheckExpression(binary.Right, scope);

        if (leftType != rightType)
            throw new Exception(
                $"Type Mismatch: Operator '{binary.Operator}' cannot be applied to types '{leftType}' and '{rightType}'");

        return leftType; // e.g. i16 + i16 yields an i16
    }

    private static bool CanAssign(string targetType, string sourceType, ExpressionNode expression)
    {
        if (targetType == sourceType) return true;

        if (expression is LiteralNode<string> literal && IsIntegerType(targetType))
            return FitsInType(literal.Value, targetType);

        if (targetType == TypeUtils.Char && expression is LiteralNode<char> _) return true;

        if (expression is UnaryExpressionNode
            {
                Operator: "-",
                Operand: LiteralNode<string> lit
            }
            && targetType is "i8" or "i16" or "i32" or "i64")
            return NegativeLiteralFitsInType(lit.Value, targetType);

        return false;
    }

    private static bool IsIntegerType(string type)
    {
        return type is "i8" or "i16" or "i32" or "i64"
            or "u8" or "u16" or "u32" or "u64";
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