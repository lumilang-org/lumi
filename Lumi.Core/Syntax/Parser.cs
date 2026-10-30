using System.Globalization;
using Lumi.Core.Ast;

namespace Lumi.Core.Syntax;

public sealed class Parser(IReadOnlyList<Token> tokens)
{
    private int _position;

    // Helper properties to read the current and upcoming tokens
    private Token Current => Peek(0);
    private Token Next => Peek(1);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        if (index >= tokens.Count)
        {
            return tokens[^1]; // Always return the final EOF token if out of range
        }

        return tokens[index];
    }

    private void Advance()
    {
        if (_position < tokens.Count - 1)
        {
            _position++;
        }
    }

    private void Consume(TokenType type)
    {
        if (Current.Type == type)
        {
            Advance();
        }
        else
        {
            throw new Exception(
                $"Syntax Error: Expected token {type}, got {Current.Type} ('{Current.Value}') at position {_position}");
        }
    }

    private Precedence GetPrecedence(TokenType type)
    {
        return type switch
        {
            TokenType.LogicalOr => Precedence.LogicalOr,

            TokenType.LogicalAnd => Precedence.LogicalAnd,

            TokenType.BitwiseOr => Precedence.BitwiseOr,

            TokenType.BitwiseXor => Precedence.BitwiseXor,

            TokenType.BitwiseAnd => Precedence.BitwiseAnd,

            TokenType.LeftShift => Precedence.Shift,

            TokenType.RightShift => Precedence.Shift,

            TokenType.EqualEqual
                or TokenType.BangEqual
                or TokenType.LessThan
                or TokenType.LessThanOrEqual
                or TokenType.GreaterThan
                or TokenType.GreaterThanOrEqual
                => Precedence.Comparison,

            TokenType.Plus or TokenType.Minus
                => Precedence.Sum,

            TokenType.Star or TokenType.Slash
                => Precedence.Product,

            _ => Precedence.Lowest
        };
    }

    public FunctionDeclarationNode ParseFunction()
    {
        Consume(TokenType.KeywordFunc);

        var funcName = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.LParen);
        var parameters = ParseFunctionParameters();
        Consume(TokenType.RParen);

        Consume(TokenType.Arrow);

        var returnType = Current.Value;
        var returnToken = Lexer.Keywords.GetValueOrDefault(returnType);

        Consume(returnToken);

        Consume(TokenType.LBrace);

        var body = new List<StatementNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            body.Add(ParseStatement());
        }

        Consume(TokenType.RBrace);

        return new FunctionDeclarationNode(
            funcName,
            parameters,
            returnType,
            body);
    }

    public ProgramNode ParseProgram()
    {
        var functions = new List<FunctionDeclarationNode>();

        while (Current.Type != TokenType.EndOfFile)
        {
            functions.Add(ParseFunction());
        }

        return new ProgramNode(functions);
    }

    private IReadOnlyList<FunctionParameterNode> ParseFunctionParameters()
    {
        var parameters = new List<FunctionParameterNode>();

        // Empty parameter list.
        if (Current.Type == TokenType.RParen)
        {
            return parameters;
        }

        while (true)
        {
            if (!IsTypeToken(Current.Type))
            {
                throw new Exception(
                    $"Syntax Error: Expected parameter type, got {Current.Type} ('{Current.Value}').");
            }

            var type = Current.Value;
            Advance();

            var name = Current.Value;
            Consume(TokenType.Identifier);

            parameters.Add(new FunctionParameterNode(type, name));

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        return parameters;
    }

    private StatementNode ParseStatement()
    {
        switch (Current.Type)
        {
            case
                TokenType.TypeI16 or
                TokenType.TypeI32 or
                TokenType.TypeI64 or
                TokenType.TypeU16 or
                TokenType.TypeU32 or
                TokenType.TypeU64 or
                TokenType.TypeF16 or
                TokenType.TypeF32 or
                TokenType.TypeF64 or
                TokenType.TypeBool or
                TokenType.TypeChar:
            {
                return ParseVariableDeclaration();
            }
            case TokenType.KeywordReturn:
            {
                Consume(TokenType.KeywordReturn);

                ExpressionNode? value = null;

                if (Current.Type != TokenType.RBrace &&
                    Current.Type != TokenType.EndOfFile)
                {
                    value = ParseExpression();
                }

                return new ReturnStatementNode(value);
            }
            case TokenType.KeywordIf:
                return ParseIfStatement();
            case TokenType.KeywordWhile:
                return ParseWhileStatement();
            case TokenType.KeywordBreak:
                Advance();
                return new BreakStatementNode();

            case TokenType.KeywordContinue:
                Advance();
                return new ContinueStatementNode();
            case TokenType.PrintFunc:
            {
                var functionName = Current.Value;
                var appendNewline = functionName == "println";

                Consume(TokenType.PrintFunc);
                Consume(TokenType.LParen);

                if (Current.Type == TokenType.RParen)
                {
                    throw new Exception(
                        $"{Current.Value}: expected an expression inside {functionName}.");
                }

                var expression = ParseExpression();

                Consume(TokenType.RParen);

                return new PrintStatementNode(expression, appendNewline);
            }
            case TokenType.Identifier when Next.Type == TokenType.Assign:
            {
                return ParseAssignmentStatement();
            }
            case TokenType.KeywordFor:
            {
                return ParseForStatement();
            }

            default:
            {
                // Treat plain expressions as expression statements (e.g., a + b)
                var expr = ParseExpression();
                return new ExpressionStatementNode(expr);
            }
        }
    }

    private ForStatementNode ParseForStatement()
    {
        Consume(TokenType.KeywordFor);

        StatementNode? initializer = null;

        if (Current.Type != TokenType.Semicolon)
        {
            initializer = IsTypeToken(Current.Type)
                ? ParseVariableDeclaration()
                : ParseAssignmentStatement();
        }

        Consume(TokenType.Semicolon);

        ExpressionNode? condition = null;

        if (Current.Type != TokenType.Semicolon)
        {
            condition = ParseExpression();
        }

        Consume(TokenType.Semicolon);

        StatementNode? update = null;

        if (Current.Type != TokenType.LBrace)
        {
            update = Current.Type == TokenType.Identifier &&
                     Next.Type == TokenType.Assign
                ? ParseAssignmentStatement()
                : new ExpressionStatementNode(ParseExpression());
        }

        var body = ParseBlock();

        return new ForStatementNode(
            initializer,
            condition,
            update,
            body);
    }

    private static bool IsTypeToken(TokenType type)
    {
        return type is
            TokenType.TypeI16 or
            TokenType.TypeI32 or
            TokenType.TypeI64 or
            TokenType.TypeU16 or
            TokenType.TypeU32 or
            TokenType.TypeU64 or
            TokenType.TypeF16 or
            TokenType.TypeF32 or
            TokenType.TypeF64 or
            TokenType.TypeBool or
            TokenType.TypeChar;
    }

    private WhileStatementNode ParseWhileStatement()
    {
        Consume(TokenType.KeywordWhile);

        var condition = ParseExpression();
        var body = ParseBlock();

        return new WhileStatementNode(condition, body);
    }

    private IfStatementNode ParseIfStatement()
    {
        Consume(TokenType.KeywordIf);

        var condition = ParseExpression();
        var thenBody = ParseBlock();

        IReadOnlyList<StatementNode>? elseBody = null;

        if (Current.Type == TokenType.KeywordElse)
        {
            Advance();

            if (Current.Type == TokenType.KeywordIf)
            {
                elseBody =
                [
                    ParseIfStatement()
                ];
            }
            else
            {
                elseBody = ParseBlock();
            }
        }

        return new IfStatementNode(condition, thenBody, elseBody);
    }

    private IReadOnlyList<StatementNode> ParseBlock()
    {
        Consume(TokenType.LBrace);

        var statements = new List<StatementNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            statements.Add(ParseStatement());
        }

        Consume(TokenType.RBrace);

        return statements;
    }

    private ExpressionNode ParseExpression(
        Precedence precedence = Precedence.Lowest)
    {
        var leftExpr = ParsePrefix();

        while (Current.Type != TokenType.EndOfFile &&
               Current.Type != TokenType.RParen &&
               precedence < GetPrecedence(Current.Type))
        {
            leftExpr = ParseInfix(leftExpr);
        }

        return leftExpr;
    }


    private ExpressionNode ParsePrefix()
    {
        return Current.Type switch
        {
            TokenType.Number => ParseNumberLiteral(),
            TokenType.Identifier => ParseIdentifier(),
            TokenType.Character => ParseCharLiteral(),
            TokenType.BooleanLiteral => ParseBooleanLiteral(),
            TokenType.LParen => ParseGroupedExpression(),
            TokenType.LogicalNot or
                TokenType.BitwiseNot or
                TokenType.LeftShift or
                TokenType.RightShift or
                TokenType.Minus => ParseUnaryExpression(),
            _ => throw new Exception($"Syntax Error: Unexpected prefix token {Current.Type}")
        };
    }

    private ExpressionNode ParseGroupedExpression()
    {
        Advance(); // Move past '('

        var expression = ParseExpression();

        if (Current.Type != TokenType.RParen)
        {
            throw new Exception("Syntax Error: Expected ')' after expression");
        }

        Advance();

        return expression;
    }

    private ExpressionNode ParseCharLiteral()
    {
        var node = new LiteralNode<char>(Current.Value[0]);
        Advance();
        return node;
    }

    private ExpressionNode ParseUnaryExpression()
    {
        var op = Current.Value;
        Advance();

        var operand = ParseExpression(Precedence.Prefix);
        return new UnaryExpressionNode(op, operand);
    }

    private ExpressionNode ParseBooleanLiteral()
    {
        var node = new LiteralNode<bool>(Current.Value.Equals("true", StringComparison.InvariantCulture));
        Advance();
        return node;
    }

    private ExpressionNode ParseInfix(ExpressionNode left)
    {
        var op = Current.Value;
        var currentPrecedence = GetPrecedence(Current.Type);

        Advance(); // Step OVER the operator token (e.g., past '+')

        // Parse the right-hand element with current binding precedence
        var right = ParseExpression(currentPrecedence);
        return new BinaryExpressionNode(left, op, right);
    }


    private ExpressionNode ParseNumberLiteral()
    {
        var value = Current.Value;
        Advance();

        if (value.Contains('.'))
        {
            if (!double.TryParse(
                    value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var decimalValue))
            {
                throw new Exception(
                    $"Syntax Error: Invalid decimal literal '{value}'");
            }

            return new LiteralNode<double>(decimalValue);
        }

        return new LiteralNode<string>(value);
    }

    private ExpressionNode ParseIdentifier()
    {
        var name = Current.Value;
        Advance();

        if (Current.Type != TokenType.LParen)
        {
            return new IdentifierNode(name);
        }

        Consume(TokenType.LParen);

        var arguments = new List<ExpressionNode>();

        if (Current.Type != TokenType.RParen)
        {
            while (true)
            {
                arguments.Add(ParseExpression());

                if (Current.Type != TokenType.Comma)
                {
                    break;
                }

                Consume(TokenType.Comma);
            }
        }

        Consume(TokenType.RParen);

        return new FunctionCallExpressionNode(name, arguments);
    }

    private VariableDeclarationNode ParseVariableDeclaration()
    {
        var type = Current.Value;
        Advance();

        var name = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.Assign);

        var initializer = ParseExpression();

        return new VariableDeclarationNode(type, name, initializer);
    }

    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var name = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.Assign);

        var value = ParseExpression();

        return new AssignmentStatementNode(name, value);
    }
}
