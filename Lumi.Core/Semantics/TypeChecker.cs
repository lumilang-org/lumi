using System.Globalization;
using Lumi.Core.Ast;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker
{
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
        }
    }

    private string CheckExpression(ExpressionNode expression, SymbolTable scope)
    {
        return expression switch
        {
            NumberLiteralNode node => InferBoundsUtils.InferNumericBounds(node),

            // Lookup identifiers inside our active tracking scope
            IdentifierNode id => scope.Lookup(id.Name),

            // Enforce safe math evaluation
            BinaryExpressionNode binary => CheckBinaryExpression(binary, scope),

            _ => throw new Exception("Unknown expression type encountered during analysis.")
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

        if (expression is NumberLiteralNode literal && IsIntegerType(targetType))
            return FitsInType(literal.Value, targetType);

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
}