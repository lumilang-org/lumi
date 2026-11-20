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
        var lexer = new Lexer(source);

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
        var lexer = new Lexer(source);

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
        var lexer = new Lexer(source);

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
        var lexer = new Lexer(source);

        var token1 = lexer.NextToken();
        Assert.That(token1.Type, Is.EqualTo(TokenType.TypeI32));

        var token2 = lexer.NextToken();
        Assert.That(token2.Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(token2.Value, Is.EqualTo("x"));
    }
}
