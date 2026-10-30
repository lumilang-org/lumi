using System.Globalization;
using Lumi.Core.Ast;
using Lumi.Core.Diagnostics;

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

    private Token Consume(TokenType type)
    {
        if (Current.Type != type)
        {
            throw new Exception(
                $"Syntax Error: Expected token {type}, got {Current.Type} ('{Current.Value}') at position {_position}");
        }

        var token = Current;
        Advance();

        return token;
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

    private FunctionDeclarationNode ParseFunction()
    {
        var start = Consume(TokenType.KeywordFunc);

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

        var closingBrace = Consume(TokenType.RBrace);

        var span = CreateSpan(
            start.Span.Start,
            closingBrace.Span.End);

        return new FunctionDeclarationNode(
            funcName,
            parameters,
            returnType,
            body,
            span);
    }

    public ProgramNode ParseProgram()
    {
        var functions = new List<FunctionDeclarationNode>();

        while (Current.Type != TokenType.EndOfFile)
        {
            functions.Add(ParseFunction());
        }

        var span = functions.Count > 0
            ? CreateSpan(
                functions[0].Span!.Start,
                functions[^1].Span!.End)
            : Current.Span;

        return new ProgramNode(functions, span);
    }

    private IReadOnlyList<FunctionParameterNode> ParseFunctionParameters()
    {
        var parameters = new List<FunctionParameterNode>();

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

            var typeToken = Current;
            var type = Current.Value;
            Advance();

            var nameToken = Current;
            var name = Current.Value;
            Consume(TokenType.Identifier);

            var span = CreateSpan(
                typeToken.Span.Start,
                nameToken.Span.End);

            parameters.Add(new FunctionParameterNode(type, name, span));

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
                var start = Current.Span.Start;
                var returnToken = Consume(TokenType.KeywordReturn);

                ExpressionNode? value = null;

                if (Current.Type != TokenType.RBrace &&
                    Current.Type != TokenType.EndOfFile)
                {
                    value = ParseExpression();
                }

                var end = value?.Span?.End ?? returnToken.Span.End;
                var span = CreateSpan(start, end);

                return new ReturnStatementNode(value, span);
            }
            case TokenType.KeywordIf:
                return ParseIfStatement();
            case TokenType.KeywordWhile:
                return ParseWhileStatement();
            case TokenType.KeywordBreak:
            {
                var token = Consume(TokenType.KeywordBreak);
                return new BreakStatementNode(token.Span);
            }

            case TokenType.KeywordContinue:
            {
                var token = Consume(TokenType.KeywordContinue);
                return new ContinueStatementNode(token.Span);
            }
            case TokenType.PrintFunc:
            {
                var start = Current.Span.Start;
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
                var closingParen = Consume(TokenType.RParen);

                var span = CreateSpan(start, closingParen.Span.End);

                return new PrintStatementNode(expression, span, appendNewline);
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
                var expr = ParseExpression();

                return new ExpressionStatementNode(
                    expr,
                    expr.Span);
            }
        }
    }

    private ForStatementNode ParseForStatement()
    {
        var start = Consume(TokenType.KeywordFor);

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
                : ParseExpressionStatement();
        }

        var body = ParseBlock(out var bodySpan);

        var span = CreateSpan(
            start.Span.Start,
            bodySpan.End);

        return new ForStatementNode(
            initializer,
            condition,
            update,
            body,
            span);
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
        var start = Consume(TokenType.KeywordWhile);

        var condition = ParseExpression();
        var body = ParseBlock(out var bodySpan);

        var span = CreateSpan(
            start.Span.Start,
            bodySpan.End);

        return new WhileStatementNode(condition, body, span);
    }

    private IfStatementNode ParseIfStatement()
    {
        var start = Consume(TokenType.KeywordIf);

        var condition = ParseExpression();
        var thenBody = ParseBlock(out var thenSpan);

        IReadOnlyList<StatementNode>? elseBody = null;
        var end = thenSpan.End;

        if (Current.Type == TokenType.KeywordElse)
        {
            Consume(TokenType.KeywordElse);

            if (Current.Type == TokenType.KeywordIf)
            {
                var nestedIf = ParseIfStatement();

                elseBody = [nestedIf];
                end = nestedIf.Span!.End;
            }
            else
            {
                elseBody = ParseBlock(out var elseSpan);
                end = elseSpan.End;
            }
        }

        var span = CreateSpan(start.Span.Start, end);

        return new IfStatementNode(
            condition,
            thenBody,
            span, elseBody);
    }

    private IReadOnlyList<StatementNode> ParseBlock(out SourceSpan span)
    {
        var openingBrace = Consume(TokenType.LBrace);

        var statements = new List<StatementNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            statements.Add(ParseStatement());
        }

        var closingBrace = Consume(TokenType.RBrace);

        span = CreateSpan(
            openingBrace.Span.Start,
            closingBrace.Span.End);

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

    private ExpressionStatementNode ParseExpressionStatement()
    {
        var expression = ParseExpression();

        return new ExpressionStatementNode(
            expression,
            expression.Span);
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
        Consume(TokenType.LParen);

        var expression = ParseExpression();

        Consume(TokenType.RParen);

        return expression;
    }

    private ExpressionNode ParseCharLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<char>(
            token.Value[0],
            token.Span);
    }

    private ExpressionNode ParseUnaryExpression()
    {
        var operatorToken = Current;
        var op = operatorToken.Value;

        Advance();

        var operand = ParseExpression(Precedence.Prefix);

        var span = CreateSpan(
            operatorToken.Span.Start,
            operand.Span!.End);

        return new UnaryExpressionNode(op, operand, span);
    }

    private ExpressionNode ParseBooleanLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<bool>(
            token.Value.Equals("true", StringComparison.InvariantCulture),
            token.Span);
    }

    private ExpressionNode ParseInfix(ExpressionNode left)
    {
        var operatorToken = Current;
        var op = operatorToken.Value;
        var currentPrecedence = GetPrecedence(Current.Type);

        Advance();

        var right = ParseExpression(currentPrecedence);

        var span = CreateSpan(
            left.Span!.Start,
            right.Span!.End);

        return new BinaryExpressionNode(left, op, right, span);
    }


    private ExpressionNode ParseNumberLiteral()
    {
        var token = Current;
        var value = token.Value;

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

            return new LiteralNode<double>(
                decimalValue,
                token.Span);
        }

        return new LiteralNode<string>(
            value,
            token.Span);
    }

    private ExpressionNode ParseIdentifier()
    {
        var identifierToken = Current;
        var name = identifierToken.Value;

        Advance();

        if (Current.Type != TokenType.LParen)
        {
            return new IdentifierNode(name, identifierToken.Span);
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

        var closingParen = Consume(TokenType.RParen);

        var span = CreateSpan(
            identifierToken.Span.Start,
            closingParen.Span.End);

        return new FunctionCallExpressionNode(name, arguments, span);
    }

    private VariableDeclarationNode ParseVariableDeclaration()
    {
        var start = Current.Span.Start;

        var type = Current.Value;
        Advance();

        var nameToken = Current;
        var name = nameToken.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.Assign);

        var initializer = ParseExpression();

        var span = CreateSpan(start, initializer.Span!.End);

        return new VariableDeclarationNode(
            type,
            name,
            initializer,
            span);
    }

    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var start = Current.Span.Start;

        var name = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.Assign);

        var value = ParseExpression();

        var span = CreateSpan(start, value.Span!.End);

        return new AssignmentStatementNode(name, value, span);
    }

    private SourceSpan CreateSpan(SourceLocation start, SourceLocation end)
    {
        return new SourceSpan(start, end);
    }

    private SourceSpan CreateSpan(SourceSpan start, SourceSpan end)
    {
        return CreateSpan(start.Start, end.End);
    }
}
