using System.Text;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed class Lexer
{
    public static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { TypeConst.Print, TokenType.PrintFunc },
        { TypeConst.Println, TokenType.PrintFunc },
        { TypeConst.Enum, TokenType.KeywordEnum },
        { TypeConst.Pub, TokenType.KeywordPub },
        { TypeConst.Switch, TokenType.KeywordSwitch },
        { TypeConst.Default, TokenType.KeywordDefault },
        { TypeConst.Let, TokenType.KeywordLet },
        { TypeConst.As, TokenType.KeywordAs },
        { TypeConst.In, TokenType.KeywordIn },
        { TypeConst.SizeOf, TokenType.KeywordSizeOf },
        { TypeConst.AlignOf, TokenType.KeywordAlignOf },
        { TypeConst.Extern, TokenType.KeywordExtern },
        { TypeConst.Nil, TokenType.KeywordNil },
        { TypeConst.Module, TokenType.KeywordModule },
        { TypeConst.Import, TokenType.KeywordImport },
        { TypeConst.Struct, TokenType.KeywordStruct },
        { TypeConst.Func, TokenType.KeywordFunc },
        { TypeConst.Return, TokenType.KeywordReturn },
        { TypeConst.While, TokenType.KeywordWhile },
        { TypeConst.For, TokenType.KeywordFor },
        { TypeConst.Break, TokenType.KeywordBreak },
        { TypeConst.Continue, TokenType.KeywordContinue },
        { TypeConst.Char, TokenType.TypeChar },
        { TypeConst.I8, TokenType.TypeI8 },
        { TypeConst.I16, TokenType.TypeI16 },
        { TypeConst.I32, TokenType.TypeI32 },
        { TypeConst.I64, TokenType.TypeI64 },
        { TypeConst.U8, TokenType.TypeU8 },
        { TypeConst.U16, TokenType.TypeU16 },
        { TypeConst.U32, TokenType.TypeU32 },
        { TypeConst.U64, TokenType.TypeU64 },
        { TypeConst.Usize, TokenType.TypeUsize },
        { TypeConst.Bool, TokenType.TypeBool },
        { TypeConst.Void, TokenType.TypeVoid },
        { TypeConst.F16, TokenType.TypeF16 },
        { TypeConst.F32, TokenType.TypeF32 },
        { TypeConst.F64, TokenType.TypeF64 },
        { TypeConst.If, TokenType.KeywordIf },
        { TypeConst.Else, TokenType.KeywordElse }
    };

    private readonly DiagnosticBag _diagnostics;
    private readonly string _fileName;
    private readonly string _source;
    private int _column = 1;
    private char _currentChar = '\0';
    private int _line = 1;
    private int _position;

    public Lexer(string source, DiagnosticBag diagnostics, string fileName)
    {
        _source = source;
        _diagnostics = diagnostics;
        _fileName = fileName;
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

        // `byte` is an alias: downstream stages only ever see `u8`.
        if (value == TypeConst.Byte)
        {
            return CreateToken(TokenType.TypeU8, TypeConst.U8, start);
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

                if (_currentChar == '>')
                {
                    Advance();
                    return CreateToken(TokenType.FatArrow, "=>", start);
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
            {
                Advance();

                if (_currentChar == '+')
                {
                    Advance();
                    return CreateToken(TokenType.PlusPlus, TypeConst.Increment, start);
                }

                return CreateToken(TokenType.Plus, "+", start);
            }

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

                if (_currentChar == '-')
                {
                    Advance();
                    return CreateToken(TokenType.MinusMinus, TypeConst.Decrement, start);
                }

                return CreateToken(TokenType.Minus, "-", start);

            case ',':
                Advance();
                return CreateToken(TokenType.Comma, ",", start);

            case ':':
                Advance();

                if (_currentChar != ':')
                {
                    return CreateToken(TokenType.Colon, ":", start);
                }

                Advance();
                return CreateToken(TokenType.DoubleColon, "::", start);

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

            case '"':
                return ReadStringLiteral();

            default:
                Advance();
                return CreateToken(TokenType.Unknown, op.ToString(), start);
        }
    }

    private Token ReadCharacterLiteral()
    {
        var start = new SourceLocation(_line, _column);
        var value = '\0';

        Advance(); // opening quote

        if (IsAtEndOfLine())
        {
            ReportAt(DiagnosticCodes.UnterminatedCharacter,
                "Syntax Error: Unterminated character literal.", start);
            return CreateToken(TokenType.Character, value.ToString(), start);
        }

        if (_currentChar == '\'')
        {
            Advance();
            ReportAt(DiagnosticCodes.EmptyCharacter,
                "Syntax Error: Empty character literal.", start);
            return CreateToken(TokenType.Character, value.ToString(), start);
        }

        if (_currentChar == '\\')
        {
            if (TryReadEscape(out var escaped) == EscapeResult.EndOfLine)
            {
                ReportAt(DiagnosticCodes.UnterminatedCharacter,
                    "Syntax Error: Unterminated character literal.", start);
                return CreateToken(TokenType.Character, value.ToString(), start);
            }

            value = escaped;
        }
        else
        {
            value = _currentChar;
            Advance();
        }

        if (_currentChar == '\'')
        {
            Advance();
            return CreateToken(TokenType.Character, value.ToString(), start);
        }

        // More than one character: skip to the closing quote so the parser can continue.
        while (!IsAtEndOfLine() && _currentChar != '\'')
        {
            Advance();
        }

        if (_currentChar == '\'')
        {
            Advance();
            ReportAt(DiagnosticCodes.MultiCharacter,
                "Syntax Error: Character literals must contain exactly one character.", start);
        }
        else
        {
            ReportAt(DiagnosticCodes.UnterminatedCharacter,
                "Syntax Error: Unterminated character literal.", start);
        }

        return CreateToken(TokenType.Character, value.ToString(), start);
    }

    // The current character must be the backslash. Consumes the whole escape sequence.
    private EscapeResult TryReadEscape(out char value)
    {
        var escapeStart = new SourceLocation(_line, _column);
        value = '\0';

        Advance(); // backslash

        if (IsAtEndOfLine())
        {
            return EscapeResult.EndOfLine;
        }

        var escaped = _currentChar;
        Advance();

        switch (escaped)
        {
            case 'n':
                value = '\n';
                return EscapeResult.Valid;
            case 'r':
                value = '\r';
                return EscapeResult.Valid;
            case 't':
                value = '\t';
                return EscapeResult.Valid;
            case '\\':
                value = '\\';
                return EscapeResult.Valid;
            case '\'':
                value = '\'';
                return EscapeResult.Valid;
            case '"':
                value = '"';
                return EscapeResult.Valid;
            case '0':
                value = '\0';
                return EscapeResult.Valid;
            default:
                _diagnostics.Report(
                    _fileName,
                    DiagnosticCodes.InvalidEscapeSequence,
                    $"Syntax Error: Invalid escape sequence '\\{escaped}'.",
                    new SourceSpan(escapeStart, new SourceLocation(_line, _column)));
                return EscapeResult.Invalid;
        }
    }

    private bool IsAtEndOfLine()
    {
        return IsAtEnd() || _currentChar is '\n' or '\r';
    }

    private void ReportAt(string code, string message, SourceLocation start)
    {
        _diagnostics.Report(
            _fileName,
            code,
            message,
            new SourceSpan(start, new SourceLocation(_line, _column)));
    }

    private Token ReadStringLiteral()
    {
        var start = new SourceLocation(_line, _column);
        var value = new StringBuilder();

        Advance(); // opening quote

        while (true)
        {
            if (IsAtEndOfLine())
            {
                ReportAt(DiagnosticCodes.UnterminatedString,
                    "Syntax Error: Unterminated string literal.", start);
                break;
            }

            if (_currentChar == '"')
            {
                Advance();
                break;
            }

            if (_currentChar != '\\')
            {
                value.Append(_currentChar);
                Advance();
                continue;
            }

            // An invalid escape is reported and dropped; an escape cut short by the end
            // of the line is reported as unterminated by the next iteration.
            if (TryReadEscape(out var escaped) == EscapeResult.Valid)
            {
                value.Append(escaped);
            }
        }

        return CreateToken(TokenType.String, value.ToString(), start);
    }

    private bool IsAtEnd()
    {
        return _position >= _source.Length;
    }

    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (!IsAtEnd())
        {
            var token = NextToken();
            if (token.Type != TokenType.EndOfFile)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(CreateToken(TokenType.EndOfFile, "", new SourceLocation(_line, _column)));
        return tokens;
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

    private enum EscapeResult
    {
        Valid,
        Invalid,
        EndOfLine
    }
}
