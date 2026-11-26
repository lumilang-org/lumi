using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class DeferTests
{
    private static (ProgramNode, DiagnosticBag) Check(string body)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("func main() -> void {\n" + body + "\n}", diagnostics, "t.lumi");
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        var program = new Parser(tokens, diagnostics, "t.lumi").ParseProgram();
        new TypeChecker(diagnostics).CheckProgram(program);
        return (program, diagnostics);
    }

    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    [Test]
    public void Parse_SingleStatementAndBlock()
    {
        var (program, diagnostics) = Check("defer println(1)\ndefer { println(2) println(3) }");

        Assert.That(diagnostics.HasErrors, Is.False);
        var single = (DeferStatementNode)program.Functions[0].Body[0];
        var block = (DeferStatementNode)program.Functions[0].Body[1];
        Assert.That(single.Body, Has.Count.EqualTo(1));
        Assert.That(block.Body, Has.Count.EqualTo(2));
    }

    [Test]
    public void Check_BodySeesVariablesInScope()
    {
        var (_, ok) = Check("i32 x = 1\ndefer println(x)");
        var (_, bad) = Check("defer println(y)");

        Assert.That(ok.HasErrors, Is.False);
        Assert.That(Count(bad, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_ReturnInsideDefer_IsRejected()
    {
        var (_, d) = Check("defer { return }");
        Assert.That(Count(d, DiagnosticCodes.InvalidDefer), Is.EqualTo(1));
    }

    [Test]
    public void Check_BreakAndContinueLeavingDefer_AreRejected()
    {
        var (_, d) = Check("while true {\n defer break\n defer continue\n}");
        Assert.That(Count(d, DiagnosticCodes.InvalidDefer), Is.EqualTo(2));
    }

    [Test]
    public void Check_LoopInsideDefer_MayBreak()
    {
        var (_, d) = Check("defer {\n while true { break }\n}");
        Assert.That(d.HasErrors, Is.False);
    }
}
