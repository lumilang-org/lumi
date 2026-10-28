namespace Lumi.Core.Syntax;

public sealed class Lexer
{
    public static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "print", TokenType.PrintFunc },
        { "println", TokenType.PrintFunc },
        { "struct", TokenType.KeywordStruct },
        { "func", TokenType.KeywordFunc },
        { "return", TokenType.KeywordReturn },
        { TypeConst.Char, TokenType.TypeChar },
        { TypeConst.I16, TokenType.TypeI16 },
        { TypeConst.I32, TokenType.TypeI32 },
        { TypeConst.I64, TokenType.TypeI64 },
        { TypeConst.U16, TokenType.TypeU16 },
        { TypeConst.U32, TokenType.TypeU32 },
        { TypeConst.U64, TokenType.TypeU64 },
        { "byte", TokenType.TypeByte },
        { "bool", TokenType.TypeBool },
        { "void", TokenType.TypeVoid },
        { TypeConst.F16, TokenType.TypeF16 },
        { TypeConst.F32, TokenType.TypeF32 },
        { TypeConst.F64, TokenType.TypeF64 }
    };

    private readonly string _source;
    private char _currentChar = '\0';
    private int _position;

    public Lexer(string source)
    {
        _source = source;
        if (_source.Length > 0) _currentChar = _source[0];
    }

    private void Advance()
    {
        _position++;
        _currentChar = _position < _source.Length ? _source[_position] : '\0';
    }

    private char Peek()
    {
        return _position + 1 < _source.Length ? _source[_position + 1] : '\0';
    }

    private void SkipWhitespaceAndComments()
    {
        while (_currentChar != '\0')
            if (char.IsWhiteSpace(_currentChar))
                Advance();
            // Handle comments starting with //
            else if (_currentChar == '/' && Peek() == '/')
                while (_currentChar != '\n' && _currentChar != '\0')
                    Advance();
            else
                break;
    }

    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (_currentChar == '\0') return new Token(TokenType.EndOfFile, "");

        // Identifiers and Keywords
        if (char.IsLetter(_currentChar) || _currentChar == '_') return ReadIdentifierOrKeyword();

        // Number Literals
        if (char.IsDigit(_currentChar)) return ReadNumber();

        // Single & Multi-character Operators
        return ReadOperatorOrSymbol();
    }

    private Token ReadIdentifierOrKeyword()
    {
        var start = _position;
        while (_currentChar != '\0' && (char.IsLetterOrDigit(_currentChar) || _currentChar == '_')) Advance();

        // Zero-allocation slice evaluation using Spans
        var lexemeSpan = _source.AsSpan(start, _position - start);
        var value = lexemeSpan.ToString();

        if (value is TypeConst.True or TypeConst.False) return new Token(TokenType.BooleanLiteral, value);

        if (Keywords.TryGetValue(value, out var matchedType)) return new Token(matchedType, value);

        return new Token(TokenType.Identifier, value);
    }

    private Token ReadNumber()
    {
        var start = _position;

        while (!IsAtEnd() && char.IsDigit(_currentChar))
            Advance();

        if (!IsAtEnd() && _currentChar == '.')
        {
            Advance();

            if (IsAtEnd() || !char.IsDigit(_currentChar))
                throw new Exception(
                    "Syntax Error: Expected a digit after decimal point.");

            while (!IsAtEnd() && char.IsDigit(_currentChar))
                Advance();
        }

        var value = _source[start.._position];
        return new Token(TokenType.Number, value);
    }

    private Token ReadOperatorOrSymbol()
    {
        var op = _currentChar;
        switch (op)
        {
            case '=':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return new Token(TokenType.EqualEqual, "==");
                }

                return new Token(TokenType.Assign, "=");

            case '!':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return new Token(TokenType.BangEqual, "!=");
                }

                return new Token(TokenType.LogicalNot, "!");

            case '&':
                Advance();

                if (_currentChar != '&')
                    throw new NotSupportedException("Unexpected character '&'");

                Advance();
                return new Token(TokenType.LogicalAnd, "&&");

            case '|':
                Advance();

                if (_currentChar != '|')
                    throw new NotSupportedException("Unexpected character '|'");

                Advance();
                return new Token(TokenType.LogicalOr, "||");


            case '<':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return new Token(TokenType.LessThanOrEqual, "<=");
                }

                return new Token(TokenType.LessThan, "<");

            case '>':
                Advance();

                if (_currentChar == '=')
                {
                    Advance();
                    return new Token(TokenType.GreaterThanOrEqual, ">=");
                }

                return new Token(TokenType.GreaterThan, ">");

            case '+':
                Advance();
                return new Token(TokenType.Plus, "+");
            case '*':
                Advance();
                return new Token(TokenType.Star, "*");
            case '/':
                Advance();
                return new Token(TokenType.Slash, "/");
            case '-':
                if (Peek() == '>')
                {
                    Advance();
                    Advance();
                    return new Token(TokenType.Arrow, "->");
                }

                Advance();
                return new Token(TokenType.Minus, "-");
            case ',':
                Advance();
                return new Token(TokenType.Comma, ",");
            case '{':
                Advance();
                return new Token(TokenType.LBrace, "{");
            case '}':
                Advance();
                return new Token(TokenType.RBrace, "}");
            case '(':
                Advance();
                return new Token(TokenType.LParen, "(");
            case ')':
                Advance();
                return new Token(TokenType.RParen, ")");
            case '[':
                Advance();
                return new Token(TokenType.LBracket, "[");
            case ']':
                Advance();
                return new Token(TokenType.RBracket, "]");
            case '\'':
                return ReadCharacterLiteral();
        }

        Advance();
        return new Token(TokenType.Unknown, op.ToString());
    }

    private Token ReadCharacterLiteral()
    {
        Advance(); // Consume opening quote

        if (IsAtEnd()) throw new Exception("Lexer Error: Unterminated character literal.");

        var value = _currentChar;

        if (value == '\'') throw new Exception("Lexer Error: Empty character literal.");

        if (value == '\\')
            throw new NotSupportedException(
                "Escape sequences in character literals are not supported yet.");

        Advance(); // Consume character

        if (IsAtEnd() || _currentChar != '\'')
            throw new Exception(
                "Lexer Error: Character literals must contain exactly one character.");

        Advance(); // Consume closing quote

        return new Token(TokenType.Character, value.ToString());
    }

    private bool IsAtEnd()
    {
        return _position >= _source.Length;
    }
}