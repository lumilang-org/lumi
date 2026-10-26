namespace Lumi.Core.Syntax;

public sealed class Lexer
{
    public Lexer(string source)
    {
        _source = source;
        if (_source.Length > 0)
        {
            _currentChar = _source[0];
        }
    }
    
    private readonly string _source;
    private int _position;
    private char _currentChar = '\0';

    public static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "print", TokenType.PrintFunc },
        { "struct", TokenType.KeywordStruct }, 
        { "func", TokenType.KeywordFunc }, 
        { "return", TokenType.KeywordReturn },
        { "char", TokenType.TypeChar }, 
        { "i16", TokenType.TypeI16 }, 
        { "i32", TokenType.TypeI32 },
        { "i64", TokenType.TypeI64 }, 
        { "u16", TokenType.TypeU16 }, 
        { "u32", TokenType.TypeU32 },
        { "u64", TokenType.TypeU64 }, 
        { "byte", TokenType.TypeByte }, 
        { "bool", TokenType.TypeBool },
        { "void", TokenType.TypeVoid }
    };

    private void Advance()
    {
        _position++;
        _currentChar = _position < _source.Length ? _source[_position] : '\0';
    }

    private char Peek()
    {
        return (_position + 1 < _source.Length) ? _source[_position + 1] : '\0';
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
            return new Token(TokenType.EndOfFile, "");
        }

        // Identifiers and Keywords
        if (char.IsLetter(_currentChar) || _currentChar == '_')
        {
            return ReadIdentifierOrKeyword();
        }

        // Number Literals
        if (char.IsDigit(_currentChar))
        {
            return ReadNumber();
        }

        // Single & Multi-character Operators
        return ReadOperatorOrSymbol();
    }

    private Token ReadIdentifierOrKeyword()
    {
        int start = _position;
        while (_currentChar != '\0' && (char.IsLetterOrDigit(_currentChar) || _currentChar == '_'))
        {
            Advance();
        }

        // Zero-allocation slice evaluation using Spans
        var lexemeSpan = _source.AsSpan(start, _position - start);
        var value = lexemeSpan.ToString();

        if (value is "true" or "false")
        {
            return new Token(TokenType.BooleanLiteral, value);
        }

        if (Keywords.TryGetValue(value, out TokenType matchedType))
        {
            return new Token(matchedType, value);
        }

        return new Token(TokenType.Identifier, value);
    }

    private Token ReadNumber()
    {
        int start = _position;
        while (_currentChar != '\0' && char.IsDigit(_currentChar))
        {
            Advance();
        }
        return new Token(TokenType.Number, _source[start.._position]);
    }

    private Token ReadOperatorOrSymbol()
    {
        var op = _currentChar;
        switch (op)
        {
            case '=': Advance(); return new Token(TokenType.Assign, "=");
            case '+': Advance(); return new Token(TokenType.Plus, "+");
            case '-':
                if (Peek() == '>')
                {
                    Advance(); Advance();
                    return new Token(TokenType.Arrow, "->");
                }
                Advance(); return new Token(TokenType.Minus, "-");
            case ',': Advance(); return new Token(TokenType.Comma, ",");
            case '{': Advance(); return new Token(TokenType.LBrace, "{");
            case '}': Advance(); return new Token(TokenType.RBrace, "}");
            case '(': Advance(); return new Token(TokenType.LParen, "(");
            case ')': Advance(); return new Token(TokenType.RParen, ")");
            case '[': Advance(); return new Token(TokenType.LBracket, "[");
            case ']': Advance(); return new Token(TokenType.RBracket, "]");
        }

        Advance();
        return new Token(TokenType.Unknown, op.ToString());
    }
}