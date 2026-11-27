using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
    // `<T, U>` after a declaration name; empty when absent.
    private List<string> ParseTypeParameters()
    {
        var names = new List<string>();

        if (Current.Type != TokenType.LessThan)
        {
            return names;
        }

        var open = Consume(TokenType.LessThan);

        if (Current.Type == TokenType.GreaterThan)
        {
            Report(
                DiagnosticCodes.InvalidTypeParameters,
                "Syntax Error: A type parameter list cannot be empty; write '<T>' or remove '<>'.",
                open.Span);
            throw new ParseException();
        }

        while (true)
        {
            var nameToken = Consume(TokenType.Identifier);

            if (names.Contains(nameToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Duplicate type parameter '{nameToken.Value}'.",
                    nameToken.Span);
            }
            else
            {
                names.Add(nameToken.Value);
            }

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        Consume(TokenType.GreaterThan);
        return names;
    }

    // `where T : Vec2, U : i32`
    private List<TypeConstraintNode> ParseWhereClause(IReadOnlyList<string> typeParameters)
    {
        var constraints = new List<TypeConstraintNode>();

        if (Current.Type != TokenType.KeywordWhere)
        {
            return constraints;
        }

        Consume(TokenType.KeywordWhere);

        while (true)
        {
            var parameterToken = Consume(TokenType.Identifier);
            Consume(TokenType.Colon);
            var boundStart = Current;
            var bound = ParseTypeName();
            var span = CreateSpan(parameterToken.Span.Start, Peek(-1).Span.End);

            if (!typeParameters.Contains(parameterToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Constraint refers to '{parameterToken.Value}', which is not a type parameter of this declaration.",
                    parameterToken.Span);
            }
            else if (constraints.Any(c => c.Parameter == parameterToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Type parameter '{parameterToken.Value}' already has a bound.",
                    boundStart.Span);
            }
            else
            {
                constraints.Add(new TypeConstraintNode(parameterToken.Value, bound, span));
            }

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        return constraints;
    }

    // `<A, B>` after a generic name in a type or expression, as the canonical text "<A, B>".
    private string ParseTypeArguments()
    {
        var open = Consume(TokenType.LessThan);

        if (Current.Type == TokenType.GreaterThan)
        {
            Report(
                DiagnosticCodes.InvalidTypeParameters,
                "Syntax Error: A type argument list cannot be empty.",
                open.Span);
            throw new ParseException();
        }

        var arguments = new List<string> { ParseTypeName() };

        while (Current.Type == TokenType.Comma)
        {
            Consume(TokenType.Comma);
            arguments.Add(ParseTypeName());
        }

        ExpectCloseAngle();
        return "<" + string.Join(", ", arguments) + ">";
    }

    // Closes a type-argument list; `>>` closes two nested lists (`Box<Box<i32>>`).
    private void ExpectCloseAngle()
    {
        if (_pendingCloseAngle > 0)
        {
            _pendingCloseAngle--;
            return;
        }

        if (Current.Type == TokenType.GreaterThan)
        {
            Advance();
            return;
        }

        if (Current.Type == TokenType.RightShift)
        {
            Advance();
            _pendingCloseAngle = 1;
            return;
        }

        Report(
            DiagnosticCodes.ExpectedToken,
            $"Syntax Error: Expected '>' to close the type argument list, got {Current.Type} ('{Current.Value}')",
            Current.Span);
        throw new ParseException();
    }

    // Returns the offset just past a well-formed `<...>` starting at `offset`, or -1.
    private int ScanTypeArguments(int offset)
    {
        if (Peek(offset).Type != TokenType.LessThan)
        {
            return -1;
        }

        var depth = 0;

        while (true)
        {
            var type = Peek(offset).Type;

            switch (type)
            {
                case TokenType.LessThan:
                    depth++;
                    break;
                case TokenType.GreaterThan:
                    depth--;
                    break;
                case TokenType.RightShift:
                    depth -= 2;
                    break;
                case TokenType.Identifier or TokenType.Star or TokenType.Comma or TokenType.LParen
                    or TokenType.RParen or TokenType.LBracket or TokenType.RBracket or TokenType.Number
                    or TokenType.DoubleColon:
                    break;
                default:
                    if (!IsTypeToken(type))
                    {
                        return -1;
                    }

                    break;
            }

            offset++;

            if (depth < 0)
            {
                return -1;
            }

            if (depth == 0)
            {
                return offset;
            }
        }
    }

    private bool IsGenericName(string name)
    {
        var separator = name.LastIndexOf("::", StringComparison.Ordinal);
        return _genericNames.Contains(separator >= 0 ? name[(separator + 2)..] : name);
    }
}
