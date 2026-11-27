using System.Globalization;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
    private ExpressionNode ParseArrayLiteral()
    {
        var startToken = Consume(TokenType.LBracket);
        var elements = new List<ExpressionNode>();

        if (Current.Type != TokenType.RBracket)
        {
            while (true)
            {
                elements.Add(ParseExpression());

                if (Current.Type != TokenType.Comma)
                {
                    break;
                }

                Consume(TokenType.Comma);
            }
        }

        var endToken = Consume(TokenType.RBracket);
        var span = CreateSpan(startToken.Span.Start, endToken.Span.End);

        return new ArrayLiteralNode(elements, span);
    }

    private ExpressionNode ParseCharLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<char>(
            token.Value[0],
            token.Span);
    }

    private ExpressionNode ParseStringLiteral()
    {
        var token = Current;
        Advance();

        return new StringLiteralNode(token.Value, token.Span);
    }

    private ExpressionNode ParseBooleanLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<bool>(
            token.Value.Equals("true", StringComparison.InvariantCulture),
            token.Span);
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
                Report(
                    DiagnosticCodes.InvalidLiteral,
                    $"Syntax Error: Invalid decimal literal '{value}'",
                    token.Span);
                throw new ParseException();
            }

            return new LiteralNode<double>(
                decimalValue,
                token.Span);
        }

        return new LiteralNode<string>(
            value,
            token.Span);
    }
}
