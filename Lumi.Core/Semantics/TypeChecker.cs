using Lumi.Core.Ast;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class TypeChecker
{
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
                // Check the type of the assignment value
                var initializerType = CheckExpression(varDecl.Initializer, scope);

                // For a simple language, enforce strict type matching
                if (varDecl.Type != initializerType)
                {
                    throw new Exception($"Type Mismatch: Cannot assign type '{initializerType}' to a variable of type '{varDecl.Type}'");
                }

                // Register variable in the current local scope
                scope.Declare(varDecl.Name, varDecl.Type);
                break;

            case ExpressionStatementNode exprStmt:
                CheckExpression(exprStmt.Expression, scope);
                break;
            case ReturnStatementNode retStmt:
                var actualReturnType = CheckExpression(retStmt.Value, scope);

                if (actualReturnType != expectedReturnType)
                {
                    throw new Exception($"Type Mismatch: Function expects to return '{expectedReturnType}' but got '{actualReturnType}' instead.");
                }
                break;
        }
    }
    
    private string CheckExpression(ExpressionNode expression, SymbolTable scope)
    {
        return expression switch
        {
            // Number literals map out to integer type processing
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
        {
            throw new Exception($"Type Mismatch: Operator '{binary.Operator}' cannot be applied to types '{leftType}' and '{rightType}'");
        }

        return leftType; // e.g. i16 + i16 yields an i16
    }
}