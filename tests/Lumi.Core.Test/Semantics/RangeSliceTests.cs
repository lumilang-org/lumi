using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class RangeSliceTests
{
    private static List<Token> Lex(string source, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(source, diagnostics, "t.lumi");
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        return tokens;
    }

    private static (ProgramNode, DiagnosticBag) Run(string body)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = Lex("func main() {\n" + body + "\n}", diagnostics);
        var program = new Parser(tokens, diagnostics, "t.lumi").ParseProgram();
        new TypeChecker(diagnostics).CheckProgram(program);
        return (program, diagnostics);
    }

    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    private static string Messages(DiagnosticBag d) => string.Join("\n", d.Diagnostics.Select(x => x.Message));

    [Test]
    public void Lex_DotDot_DoesNotBreakMemberAccessOrNumbers()
    {
        var tokens = Lex("0..3 a..b s.len x.0 1.5 t.0..2", new DiagnosticBag()).Select(t => (t.Type, t.Value)).ToList();

        Assert.That(tokens.Take(3), Is.EqualTo(new[]
        {
            (TokenType.Number, "0"), (TokenType.DotDot, ".."), (TokenType.Number, "3")
        }));
        Assert.That(tokens.Skip(3).Take(3).Select(t => t.Type),
            Is.EqualTo(new[] { TokenType.Identifier, TokenType.DotDot, TokenType.Identifier }));
        Assert.That(tokens.Skip(6).Take(3).Select(t => t.Type),
            Is.EqualTo(new[] { TokenType.Identifier, TokenType.Dot, TokenType.Identifier }));
        Assert.That(tokens.Any(t => t is { Type: TokenType.Number, Value: "1.5" }), Is.True);
    }

    [Test]
    public void Parse_RangeForms_AndPlainIndexing()
    {
        var (program, d) = Run(@"
i32[4] a = [1, 2, 3, 4]
i32 n = 1
let full = a[n + 1 .. n * 2 + 1]
let tail = a[n..]
let head = a[..2]
let all = a[..]
let one = a[n]");

        Assert.That(d.HasErrors, Is.False, Messages(d));
        var body = program.Functions[0].Body.Cast<VariableDeclarationNode>().ToList();
        var full = (RangeIndexExpressionNode)body[2].Initializer!;
        Assert.That(full.Start, Is.InstanceOf<BinaryExpressionNode>());
        Assert.That(full.End, Is.InstanceOf<BinaryExpressionNode>());
        var tail = (RangeIndexExpressionNode)body[3].Initializer!;
        Assert.That(tail.Start, Is.Not.Null);
        Assert.That(tail.End, Is.Null);
        Assert.That(((RangeIndexExpressionNode)body[4].Initializer!).Start, Is.Null);
        var all = (RangeIndexExpressionNode)body[5].Initializer!;
        Assert.That(all.Start, Is.Null);
        Assert.That(all.End, Is.Null);
        Assert.That(body[6].Initializer, Is.InstanceOf<IndexExpressionNode>());
    }

    [Test]
    public void Check_RangeYieldsSlice_OnSlicesAndArrays_AndChains()
    {
        var (program, d) = Run(@"
i32[4] a = [1, 2, 3, 4]
i32[] s = a
i32[] x = s[1..3]
i32[] y = a[1..3]
i32[] z = s[1..4][0..2]
i64 n = s[1..3].len
let m = a[..]");

        Assert.That(d.HasErrors, Is.False, Messages(d));
        Assert.That(((VariableDeclarationNode)program.Functions[0].Body[^1]).Type, Is.EqualTo("let"));
    }

    [Test]
    public void Check_RangeErrors()
    {
        var (_, nonInteger) = Run("i32[2] a = [1, 2]\nlet r = a[true..1]");
        var (_, notArray) = Run("i32 n = 3\nlet r = n[0..1]");
        var (_, reversed) = Run("i32[4] a = [1, 2, 3, 4]\nlet r = a[3..1]");
        var (_, tooLong) = Run("i32[4] a = [1, 2, 3, 4]\nlet r = a[1..5]");
        var (_, negative) = Run("i32[] s = [1, 2]\nlet r = s[-1..1]");
        var (_, reversedSlice) = Run("i32[] s = [1, 2]\nlet r = s[2..1]");
        var (_, openOk) = Run("i32[4] a = [1, 2, 3, 4]\nlet r = a[4..]\nlet q = a[..4]");

        Assert.That(Count(nonInteger, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(notArray, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(reversed, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(Count(tooLong, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(Count(negative, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(Count(reversedSlice, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(openOk.HasErrors, Is.False, Messages(openOk));
    }
}
