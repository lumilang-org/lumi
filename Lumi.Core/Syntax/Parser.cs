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
        if (index >= tokens.Count) return tokens[^1]; // Always return the final EOF token if out of range
        return tokens[index];
    }

    private void Advance()
    {
        if (_position < tokens.Count - 1) _position++;
    }

    private void Consume(TokenType type)
    {
        if (Current.Type == type)
            Advance();
        else
            throw new Exception(
                $"Syntax Error: Expected token {type}, got {Current.Type} ('{Current.Value}') at position {_position}");
    }

    private Precedence GetPrecedence(TokenType type)
    {
        return type switch
        {
            TokenType.Plus or TokenType.Minus => Precedence.Sum,
            _ => Precedence.Lowest
        };
    }

    // --- Parsing Entry Points ---

    public FunctionDeclarationNode ParseFunction()
    {
        Consume(TokenType.KeywordFunc);

        var funcName = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.LParen);
        Consume(TokenType.RParen);
        Consume(TokenType.Arrow);

        var returnType = Current.Value;
        var returnToken = Lexer.Keywords.GetValueOrDefault(returnType);

        Consume(returnToken);

        Consume(TokenType.LBrace);

        var body = new List<StatementNode>();
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EndOfFile) body.Add(ParseStatement());

        Consume(TokenType.RBrace);

        return new FunctionDeclarationNode(funcName, returnType, body);
    }

    private StatementNode ParseStatement()
    {
        switch (Current.Type)
        {
            // Simple type token routing for variable declarations (e.g., i16 a = 10)
            case TokenType.TypeI16 or TokenType.TypeI32 or TokenType.TypeI64 or TokenType.TypeVoid:
            {
                var type = Current.Value;
                Advance(); // Consume the type keyword

                var name = Current.Value;
                Consume(TokenType.Identifier);

                Consume(TokenType.Assign);

                var initializer = ParseExpression();
                return new VariableDeclarationNode(type, name, initializer);
            }
            case TokenType.KeywordReturn:
            {
                Consume(TokenType.KeywordReturn);

                var value = ParseExpression();
                return new ReturnStatementNode(value);
            }
            case TokenType.PrintFunc:
            {
                Consume(TokenType.PrintFunc);
                Consume(TokenType.LParen);

                var expression = ParseExpression();

                Consume(TokenType.RParen);

                return new PrintStatementNode(expression);
            }
            default:
            {
                // Treat plain expressions as expression statements (e.g., a + b)
                var expr = ParseExpression();
                return new ExpressionStatementNode(expr);
            }
        }
    }

    private ExpressionNode ParseExpression(Precedence precedence = Precedence.Lowest)
    {
        // Parse the leftmost component (e.g., variable identifier or number literal)
        var leftExpr = ParsePrefix();

        // Look at Current operator type, not Next token lookahead type
        while (Current.Type != TokenType.EndOfFile && precedence < GetPrecedence(Current.Type))
            // Store operator context
            leftExpr = ParseInfix(leftExpr);

        return leftExpr;
    }


    private ExpressionNode ParsePrefix()
    {
        return Current.Type switch
        {
            TokenType.Number => ParseNumberLiteral(),
            TokenType.Identifier => ParseIdentifier(),
            _ => throw new Exception($"Syntax Error: Unexpected prefix token {Current.Type}")
        };
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
        var node = new NumberLiteralNode(Current.Value);
        Advance();
        return node;
    }

    private ExpressionNode ParseIdentifier()
    {
        var node = new IdentifierNode(Current.Value);
        Advance();
        return node;
    }
}