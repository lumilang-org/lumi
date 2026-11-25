using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class SwitchTests
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
    public void Parse_SwitchArms_PatternsBodiesAndDefault()
    {
        var (program, diagnostics) = Check(@"
i32 x = 2
switch x {
    1 => println(1)
    2, 3 => { println(2) println(3) }
    default => println(0)
}");

        Assert.That(diagnostics.HasErrors, Is.False, string.Join("\n", diagnostics.Diagnostics.Select(d => d.Message)));
        var sw = (SwitchStatementNode)program.Functions[0].Body[1];
        Assert.That(sw.Arms, Has.Count.EqualTo(2));
        Assert.That(sw.Arms[0].Patterns, Has.Count.EqualTo(1));
        Assert.That(sw.Arms[1].Patterns, Has.Count.EqualTo(2));
        Assert.That(sw.Arms[1].Body, Has.Count.EqualTo(2));
        Assert.That(sw.DefaultBody, Is.Not.Null);
    }

    [Test]
    public void Check_IntegerCharAndBoolSubjects_AreAccepted()
    {
        var (_, ints) = Check("u8 b = 1\nswitch b { 1 => println(1) 255 => println(2) }");
        var (_, chars) = Check("char c = 'a'\nswitch c { 'a' => println(1) 'b' => println(2) }");
        var (_, bools) = Check("bool f = true\nswitch f { true => println(1) false => println(2) }");
        var (_, negative) = Check("i32 n = 1\nswitch n { -1 => println(1) default => println(2) }");

        foreach (var d in new[] { ints, chars, bools, negative })
        {
            Assert.That(d.HasErrors, Is.False, string.Join("\n", d.Diagnostics.Select(x => x.Message)));
        }
    }

    [Test]
    public void Check_DuplicateCases_AreRejected_AcrossArms()
    {
        var (_, sameArm) = Check("i32 x = 1\nswitch x { 1, 1 => println(1) }");
        var (_, acrossArms) = Check("i32 x = 1\nswitch x { 1 => println(1) 2, 1 => println(2) }");

        Assert.That(Count(sameArm, DiagnosticCodes.DuplicateCase), Is.EqualTo(1));
        Assert.That(Count(acrossArms, DiagnosticCodes.DuplicateCase), Is.EqualTo(1));
    }

    [Test]
    public void Check_CaseTypeMismatches_AndUnsupportedSubjects()
    {
        var (_, charOnInt) = Check("i32 x = 1\nswitch x { 'a' => println(1) }");
        var (_, tooBig) = Check("u8 x = 1\nswitch x { 300 => println(1) }");
        var (_, boolOnInt) = Check("i32 x = 1\nswitch x { true => println(1) }");
        var (_, str) = Check("string s = \"a\"\nswitch s { \"a\" => println(1) }");
        var (_, nonConstant) = Check("i32 x = 1\ni32 y = 2\nswitch x { y => println(1) }");

        Assert.That(Count(charOnInt, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(tooBig, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(boolOnInt, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(str, DiagnosticCodes.InvalidSwitch), Is.GreaterThan(0));
        Assert.That(Count(nonConstant, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ArmBodies_HaveTheirOwnScope()
    {
        var (_, separate) = Check(@"
i32 x = 1
switch x {
    1 => { i32 a = 1 println(a) }
    2 => { i32 a = 2 println(a) }
    default => { i32 a = 3 println(a) }
}");
        var (_, leaked) = Check(@"
i32 x = 1
switch x {
    1 => { i32 a = 1 }
    default => println(a)
}");

        Assert.That(separate.HasErrors, Is.False, string.Join("\n", separate.Diagnostics.Select(d => d.Message)));
        Assert.That(Count(leaked, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_Break_IsAllowedInSwitch_ContinueIsNot()
    {
        var (_, brk) = Check("i32 x = 1\nswitch x { 1 => break }");
        var (_, cont) = Check("i32 x = 1\nswitch x { 1 => continue }");
        var (_, loop) = Check("i32 x = 1\nwhile true { switch x { 1 => continue default => break } }");

        Assert.That(brk.HasErrors, Is.False);
        Assert.That(Count(cont, DiagnosticCodes.ContinueOutsideLoop), Is.EqualTo(1));
        Assert.That(loop.HasErrors, Is.False);
    }

    [Test]
    public void Parse_TwoDefaults_IsAnError()
    {
        var (_, diagnostics) = Check("i32 x = 1\nswitch x { default => println(1) default => println(2) }");
        Assert.That(Count(diagnostics, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
    }

    [Test]
    public void Check_SwitchWhereEveryArmReturns_SatisfiesMissingReturn()
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer("func f(i32 x) -> i32 { switch x { 1 => return 1 default => return 2 } }", diagnostics, "t.lumi");
        var tokens = new List<Token>();
        Token token;
        do { token = lexer.NextToken(); tokens.Add(token); } while (token.Type != TokenType.EndOfFile);
        var program = new Parser(tokens, diagnostics, "t.lumi").ParseProgram();
        new TypeChecker(diagnostics).CheckProgram(program);

        Assert.That(diagnostics.HasErrors, Is.False, string.Join("\n", diagnostics.Diagnostics.Select(d => d.Message)));
    }
}
