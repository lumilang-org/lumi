using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Syntax;

[TestFixture]
public class LexerTests
{
    [Test]
    public void NextToken_Keywords_ReturnsCorrectTokens()
    {
        var source =
            "struct func i8 i16 i32 i64 u8 u16 u32 u64 f16 f32 f64 bool void if else return while for break continue print println";
        var lexer = new Lexer(source, new DiagnosticBag(), "test.lumi");

        var expectedTypes = new[]
        {
            TokenType.KeywordStruct, TokenType.KeywordFunc,
            TokenType.TypeI8, TokenType.TypeI16, TokenType.TypeI32, TokenType.TypeI64,
            TokenType.TypeU8, TokenType.TypeU16, TokenType.TypeU32, TokenType.TypeU64,
            TokenType.TypeF16, TokenType.TypeF32, TokenType.TypeF64,
            TokenType.TypeBool, TokenType.TypeVoid,
            TokenType.KeywordIf, TokenType.KeywordElse,
            TokenType.KeywordReturn, TokenType.KeywordWhile, TokenType.KeywordFor,
            TokenType.KeywordBreak, TokenType.KeywordContinue,
            TokenType.PrintFunc, TokenType.PrintFunc
        };

        foreach (var expectedType in expectedTypes)
        {
            var token = lexer.NextToken();
            Assert.That(token.Type, Is.EqualTo(expectedType),
                $"Expected {expectedType} but got {token.Type} for '{token.Value}'");
        }
    }

    [Test]
    public void NextToken_Symbols_ReturnsCorrectTokens()
    {
        var source = "{ } ( ) [ ] , : . ; = == ! != < <= > >= + - * / -> && || & | ^ ~ << >>";
        var lexer = new Lexer(source, new DiagnosticBag(), "test.lumi");

        var expectedTypes = new[]
        {
            TokenType.LBrace, TokenType.RBrace, TokenType.LParen, TokenType.RParen,
            TokenType.LBracket, TokenType.RBracket, TokenType.Comma, TokenType.Colon,
            TokenType.Dot, TokenType.Semicolon, TokenType.Assign, TokenType.EqualEqual,
            TokenType.LogicalNot, TokenType.BangEqual, TokenType.LessThan, TokenType.LessThanOrEqual,
            TokenType.GreaterThan, TokenType.GreaterThanOrEqual, TokenType.Plus, TokenType.Minus,
            TokenType.Star, TokenType.Slash, TokenType.Arrow, TokenType.LogicalAnd,
            TokenType.LogicalOr, TokenType.BitwiseAnd, TokenType.BitwiseOr, TokenType.BitwiseXor,
            TokenType.BitwiseNot, TokenType.LeftShift, TokenType.RightShift
        };

        foreach (var expectedType in expectedTypes)
        {
            var token = lexer.NextToken();
            Assert.That(token.Type, Is.EqualTo(expectedType),
                $"Expected {expectedType} but got {token.Type} for '{token.Value}'");
        }
    }

    [Test]
    public void NextToken_Literals_ReturnsCorrectTokens()
    {
        var source = "123 45.67 true false 'a' identifier";
        var lexer = new Lexer(source, new DiagnosticBag(), "test.lumi");

        var token1 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token1.Type, Is.EqualTo(TokenType.Number));
            Assert.That(token1.Value, Is.EqualTo("123"));
        });

        var token2 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token2.Type, Is.EqualTo(TokenType.Number));
            Assert.That(token2.Value, Is.EqualTo("45.67"));
        });

        var token3 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token3.Type, Is.EqualTo(TokenType.BooleanLiteral));
            Assert.That(token3.Value, Is.EqualTo("true"));
        });

        var token4 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token4.Type, Is.EqualTo(TokenType.BooleanLiteral));
            Assert.That(token4.Value, Is.EqualTo("false"));
        });

        var token5 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token5.Type, Is.EqualTo(TokenType.Character));
            Assert.That(token5.Value, Is.EqualTo("a"));
        });

        var token6 = lexer.NextToken();
        Assert.Multiple(() =>
        {
            Assert.That(token6.Type, Is.EqualTo(TokenType.Identifier));
            Assert.That(token6.Value, Is.EqualTo("identifier"));
        });
    }

    [Test]
    public void NextToken_Comments_AreSkipped()
    {
        var source = "i32 // this is a comment\nx";
        var lexer = new Lexer(source, new DiagnosticBag(), "test.lumi");

        var token1 = lexer.NextToken();
        Assert.That(token1.Type, Is.EqualTo(TokenType.TypeI32));

        var token2 = lexer.NextToken();
        Assert.That(token2.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token2.Value, Is.EqualTo("x"));
    }

    [Test]
    public void NextToken_StringLiteral_ReturnsStringToken()
    {
        var lexer = new Lexer("\"hello world\" x", new DiagnosticBag(), "test.lumi");

        var token = lexer.NextToken();

        Assert.That(token.Type, Is.EqualTo(TokenType.String));
        Assert.That(token.Value, Is.EqualTo("hello world"));
        Assert.That(lexer.NextToken().Type, Is.EqualTo(TokenType.Identifier));
    }

    [Test]
    public void NextToken_StringLiteral_Empty()
    {
        var token = new Lexer("\"\"", new DiagnosticBag(), "test.lumi").NextToken();

        Assert.That(token.Type, Is.EqualTo(TokenType.String));
        Assert.That(token.Value, Is.Empty);
    }

    [Test]
    public void NextToken_StringLiteral_UnescapesSequences()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("\"a\\n\\r\\t\\\\\\\"\\0b\"", diagnostics, "test.lumi");

        var token = lexer.NextToken();

        Assert.That(diagnostics.HasErrors, Is.False);
        Assert.That(token.Value, Is.EqualTo("a\n\r\t\\\"\0b"));
    }

    [Test]
    public void NextToken_StringLiteral_Unterminated_ReportsDiagnosticWithFile()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("\"abc", diagnostics, "broken.lumi");

        lexer.NextToken();

        Assert.That(diagnostics.Diagnostics, Has.Count.EqualTo(1));
        var diagnostic = diagnostics.Diagnostics[0];
        Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCodes.UnterminatedString));
        Assert.That(diagnostic.FileName, Is.EqualTo("broken.lumi"));
        Assert.That(diagnostic.Span!.Start.Line, Is.EqualTo(1));
        Assert.That(diagnostic.Span.Start.Column, Is.EqualTo(1));
    }

    [Test]
    public void NextToken_StringLiteral_StopsAtNewline()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("\"abc\nx", diagnostics, "test.lumi");

        var token = lexer.NextToken();

        Assert.That(token.Value, Is.EqualTo("abc"));
        Assert.That(diagnostics.Diagnostics[0].Code, Is.EqualTo(DiagnosticCodes.UnterminatedString));
        Assert.That(lexer.NextToken().Value, Is.EqualTo("x"));
    }

    [Test]
    public void NextToken_StringLiteral_InvalidEscape_ReportsDiagnosticWithFile()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("\"a\\qb\"", diagnostics, "bad.lumi");

        var token = lexer.NextToken();

        Assert.That(diagnostics.Diagnostics, Has.Count.EqualTo(1));
        var diagnostic = diagnostics.Diagnostics[0];
        Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCodes.InvalidEscapeSequence));
        Assert.That(diagnostic.FileName, Is.EqualTo("bad.lumi"));
        Assert.That(diagnostic.Span!.Start.Column, Is.EqualTo(3));
        Assert.That(token.Value, Is.EqualTo("ab"));
    }

    [TestCase("'a'", "a")]
    [TestCase("'\\n'", "\n")]
    [TestCase("'\\r'", "\r")]
    [TestCase("'\\t'", "\t")]
    [TestCase("'\\\\'", "\\")]
    [TestCase("'\\''", "'")]
    [TestCase("'\\\"'", "\"")]
    [TestCase("'\\0'", "\0")]
    public void NextToken_CharacterLiteral_DecodesEscapes(string source, string expected)
    {
        var diagnostics = new DiagnosticBag();

        var token = new Lexer(source, diagnostics, "test.lumi").NextToken();

        Assert.That(diagnostics.HasErrors, Is.False);
        Assert.That(token.Type, Is.EqualTo(TokenType.Character));
        Assert.That(token.Value, Is.EqualTo(expected));
    }

    [TestCase("''", DiagnosticCodes.EmptyCharacter)]
    [TestCase("'ab'", DiagnosticCodes.MultiCharacter)]
    [TestCase("'a", DiagnosticCodes.UnterminatedCharacter)]
    [TestCase("'", DiagnosticCodes.UnterminatedCharacter)]
    [TestCase("'ab", DiagnosticCodes.UnterminatedCharacter)]
    public void NextToken_CharacterLiteral_Errors_ReportDiagnosticWithFile(string source, string code)
    {
        var diagnostics = new DiagnosticBag();

        new Lexer(source, diagnostics, "chars.lumi").NextToken();

        Assert.That(diagnostics.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics.Diagnostics[0].Code, Is.EqualTo(code));
        Assert.That(diagnostics.Diagnostics[0].FileName, Is.EqualTo("chars.lumi"));
        Assert.That(diagnostics.Diagnostics[0].Span!.Start.Column, Is.EqualTo(1));
    }

    [Test]
    public void NextToken_CharacterLiteral_InvalidEscape_PointsAtTheEscape()
    {
        var diagnostics = new DiagnosticBag();

        new Lexer("'\\q'", diagnostics, "chars.lumi").NextToken();

        Assert.That(diagnostics.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics.Diagnostics[0].Code, Is.EqualTo(DiagnosticCodes.InvalidEscapeSequence));
        Assert.That(diagnostics.Diagnostics[0].FileName, Is.EqualTo("chars.lumi"));
        Assert.That(diagnostics.Diagnostics[0].Span!.Start.Column, Is.EqualTo(2));
    }

    [Test]
    public void NextToken_CharacterLiteral_ErrorsDoNotSwallowFollowingTokens()
    {
        var lexer = new Lexer("'ab' x", new DiagnosticBag(), "test.lumi");

        lexer.NextToken();

        Assert.That(lexer.NextToken().Value, Is.EqualTo("x"));
    }

    [Test]
    public void NextToken_IncrementAndDecrement_AreSingleTokens()
    {
        var lexer = new Lexer("++ -- + - +  + - -> a+++b", new DiagnosticBag(), "test.lumi");

        var types = new List<TokenType>();
        Token token;
        while ((token = lexer.NextToken()).Type != TokenType.EndOfFile)
        {
            types.Add(token.Type);
        }

        Assert.That(types, Is.EqualTo(new[]
        {
            TokenType.PlusPlus, TokenType.MinusMinus, TokenType.Plus, TokenType.Minus,
            TokenType.Plus, TokenType.Plus, TokenType.Minus, TokenType.Arrow,
            TokenType.Identifier, TokenType.PlusPlus, TokenType.Plus, TokenType.Identifier
        }));
    }

    [Test]
    public void NextToken_IncrementAndDecrement_CarryOperatorText()
    {
        var lexer = new Lexer("++ --", new DiagnosticBag(), "test.lumi");

        Assert.That(lexer.NextToken().Value, Is.EqualTo("++"));
        Assert.That(lexer.NextToken().Value, Is.EqualTo("--"));
    }
}
