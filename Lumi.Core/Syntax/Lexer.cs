using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed class Lexer
{
    public static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "print", TokenType.PrintFunc },
        { "println", TokenType.PrintFunc },
        { TypeConst.Struct, TokenType.KeywordStruct },
        { "func", TokenType.KeywordFunc },
        { TypeConst.Return, TokenType.KeywordReturn },
        { TypeConst.While, TokenType.KeywordWhile },
        { TypeConst.For, TokenType.KeywordFor },
        { TypeConst.Break, TokenType.KeywordBreak },
        { TypeConst.Continue, TokenType.KeywordContinue },
        { TypeConst.Char, TokenType.TypeChar },
        { TypeConst.I16, TokenType.TypeI16 },
        { TypeConst.I32, TokenType.TypeI32 },
        { TypeConst.I64, TokenType.TypeI64 },
        { TypeConst.U16, TokenType.TypeU16 },
        { TypeConst.U32, TokenType.TypeU32 },
        { TypeConst.U64, TokenType.TypeU64 },
        { "byte", TokenType.TypeByte },
        { TypeConst.Bool, TokenType.TypeBool },
        { TypeConst.Void, TokenType.TypeVoid },
        { TypeConst.F16, TokenType.TypeF16 },
        { TypeConst.F32, TokenType.TypeF32 },
        { TypeConst.F64, TokenType.TypeF64 },
        { TypeConst.If, TokenType.KeywordIf },
        { TypeConst.Else, TokenType.KeywordElse }
    };

    private readonly string _source;
    private int _column = 1;
    private char _currentChar = '\0';
    private int _line = 1;
    private int _position;

    public Lexer(string source)
    {
        _source = source;
        if (_source.Length > 0)
        {
            _currentChar = _source[0];
        }
    }

    private void Advance()
    {
        if (_currentChar == '\r')
        {
            _position++;

            // Treat CRLF as a single newline.
            if (_position < _source.Length &&
                _source[_position] == '\n')
            {
                _position++;
            }

            _line++;
            _column = 1;
        }
        else if (_currentChar == '\n')
        {
            _position++;
            _line++;
            _column = 1;
        }
        else
        {
            _position++;
            _column++;
        }

        _currentChar = _position < _source.Length
            ? _source[_position]
            : '\0';
    }

    private char Peek()
    {
        return _position + 1 < _source.Length ? _source[_position + 1] : '\0';
    }

    private void SkipWhitespaceAndComments()
    {
        while (_currentChar != '\0')
        {
            if (char.IsWhiteSpace(_currentChar))
            {
                Advance();
            }
            // Handle comments starting with //
            else if (_currentChar == '/' && Peek() == '/')
            {
                while (_currentChar != '\n' && _currentChar != '\0')
                {
                    Advance();
                }
            }
            else
            {
                break;
            }
        }
    }

    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (_currentChar == '\0')
        {
            var location = new SourceLocation(_line, _column);
            return CreateToken(TokenType.EndOfFile, string.Empty, location);
        }

        if (char.IsLetter(_currentChar) || _currentChar == '_')
        {
            return ReadIdentifierOrKeyword();
        }

        if (char.IsDigit(_currentChar))
        {
            return ReadNumber();
        }

        return ReadOperatorOrSymbol();
    }

    private Token ReadIdentifierOrKeyword()
    {
        var start = new SourceLocation(_line, _column);
        var startPosition = _position;

        while (_currentChar != '\0' &&
               (char.IsLetterOrDigit(_currentChar) || _currentChar == '_'))
        {
            Advance();
        }

        var value = _source[startPosition.._position];

        if (value is TypeConst.True or TypeConst.False)
        {
            return CreateToken(TokenType.BooleanLiteral, value, start);
        }

        if (Keywords.TryGetValue(value, out var matchedType))
        {
            return CreateToken(matchedType, value, start);
        }

        return CreateToken(TokenType.Identifier, value, start);
    }

    private Token ReadNumber()
    {
        var start = new SourceLocation(_line, _column);
        var startPosition = _position;

        while (!IsAtEnd() && char.IsDigit(_currentChar))
        {
            Advance();
        }

        if (!IsAtEnd() && _currentChar == '.')
        {
            Advance();

            if (IsAtEnd() || !char.IsDigit(_currentChar))
            {
                throw new Exception(
                    "Syntax Error: Expected a digit after decimal point.");
            }

            while (!IsAtEnd() && char.IsDigit(_currentChar))
            {
                Advance();
            }
        }

        var value = _source[startPosition.._position];

        return CreateToken(TokenType.Number, value, start);
    }

    private Token ReadOperatorOrSymbol()
    {
        var start = new SourceLocation(_line, _column);
        var op = _currentChar;

        switch (op)
        {
            case '=':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return CreateToken(TokenType.EqualEqual, "==", start);
                }

                return CreateToken(TokenType.Assign, "=", start);

            case '!':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return CreateToken(TokenType.BangEqual, "!=", start);
                }

                return CreateToken(TokenType.LogicalNot, "!", start);

            case '&':
                Advance();

                if (_currentChar == '&')
                {
                    Advance();
                    return CreateToken(TokenType.LogicalAnd, "&&", start);
                }

                return CreateToken(TokenType.BitwiseAnd, "&", start);

            case '|':
                Advance();

                if (_currentChar == '|')
                {
                    Advance();
                    return CreateToken(TokenType.LogicalOr, TypeConst.LogicalOr, start);
                }

                return CreateToken(TokenType.BitwiseOr, TypeConst.Pipe, start);

            case '^':
                Advance();
                return CreateToken(TokenType.BitwiseXor, "^", start);

            case '~':
                Advance();
                return CreateToken(TokenType.BitwiseNot, "~", start);

            case '<':
                Advance();

                if (_currentChar == '<')
                {
                    Advance();
                    return CreateToken(TokenType.LeftShift, "<<", start);
                }

                if (_currentChar == '=')
                {
                    Advance();
                    return CreateToken(TokenType.LessThanOrEqual, "<=", start);
                }

                return CreateToken(TokenType.LessThan, "<", start);

            case '>':
                Advance();

                if (_currentChar == '>')
                {
                    Advance();
                    return CreateToken(TokenType.RightShift, ">>", start);
                }

                if (_currentChar == '=')
                {
                    Advance();
                    return CreateToken(TokenType.GreaterThanOrEqual, ">=", start);
                }

                return CreateToken(TokenType.GreaterThan, ">", start);

            case '+':
                Advance();
                return CreateToken(TokenType.Plus, "+", start);

            case '*':
                Advance();
                return CreateToken(TokenType.Star, "*", start);

            case '/':
                Advance();
                return CreateToken(TokenType.Slash, "/", start);

            case '-':
                if (Peek() == '>')
                {
                    Advance();
                    Advance();
                    return CreateToken(TokenType.Arrow, "->", start);
                }

                Advance();
                return CreateToken(TokenType.Minus, "-", start);

            case ',':
                Advance();
                return CreateToken(TokenType.Comma, ",", start);

            case ':':
                Advance();
                return CreateToken(TokenType.Colon, ":", start);

            case '.':
                Advance();
                return CreateToken(TokenType.Dot, ".", start);

            case ';':
                Advance();
                return CreateToken(TokenType.Semicolon, ";", start);

            case '{':
                Advance();
                return CreateToken(TokenType.LBrace, "{", start);

            case '}':
                Advance();
                return CreateToken(TokenType.RBrace, "}", start);

            case '(':
                Advance();
                return CreateToken(TokenType.LParen, "(", start);

            case ')':
                Advance();
                return CreateToken(TokenType.RParen, ")", start);

            case '[':
                Advance();
                return CreateToken(TokenType.LBracket, "[", start);

            case ']':
                Advance();
                return CreateToken(TokenType.RBracket, "]", start);

            case '\'':
                return ReadCharacterLiteral();

            default:
                Advance();
                return CreateToken(TokenType.Unknown, op.ToString(), start);
        }
    }

    private Token ReadCharacterLiteral()
    {
        var start = new SourceLocation(_line, _column);

        Advance();

        if (IsAtEnd())
        {
            throw new Exception("Lexer Error: Unterminated character literal.");
        }

        var value = _currentChar;

        if (value == '\'')
        {
            throw new Exception("Lexer Error: Empty character literal.");
        }

        if (value == '\\')
        {
            throw new NotSupportedException(
                "Escape sequences in character literals are not supported yet.");
        }

        Advance();

        if (IsAtEnd() || _currentChar != '\'')
        {
            throw new Exception(
                "Lexer Error: Character literals must contain exactly one character.");
        }

        Advance();

        return CreateToken(TokenType.Character, value.ToString(), start);
    }

    private bool IsAtEnd()
    {
        return _position >= _source.Length;
    }

    private Token CreateToken(
        TokenType type,
        string value,
        SourceLocation start)
    {
        var end = new SourceLocation(_line, _column);

        return new Token(
            type,
            value,
            new SourceSpan(start, end));
    }
}
