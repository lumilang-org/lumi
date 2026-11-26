using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class OptionResultTests
{
    private static DiagnosticBag Check(string body, string top = "")
    {
        var diagnostics = new DiagnosticBag();
        var source = top + "\nfunc main() {\n" + body + "\n}";
        var lexer = new Lexer(source, diagnostics, "t.lumi");
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        var program = new Parser(tokens, diagnostics, "t.lumi").ParseProgram();
        new TypeChecker(diagnostics).CheckProgram(program);
        return diagnostics;
    }

    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    private static string Messages(DiagnosticBag d) => string.Join("\n", d.Diagnostics.Select(x => x.Message));

    private const string ParseError = "enum ParseError { EmptyInput BadDigit }";

    [Test]
    public void Registry_DescribesTheBuiltins()
    {
        Assert.That(BuiltinGenericRegistry.TryGet("Option", out var option), Is.True);
        Assert.That(option.TypeParams, Is.EqualTo(new[] { "T" }));
        Assert.That(option.Variants.Select(v => (v.Name, v.TypeParamIndex, v.Tag)),
            Is.EqualTo(new (string, int?, int)[] { ("Some", 0, 0), ("None", null, 1) }));

        Assert.That(BuiltinGenericRegistry.TryGet("Result", out var result), Is.True);
        Assert.That(result.TypeParams, Is.EqualTo(new[] { "T", "E" }));
        Assert.That(result.Variants.Select(v => (v.Name, v.TypeParamIndex, v.Tag)),
            Is.EqualTo(new (string, int?, int)[] { ("Ok", 0, 0), ("Err", 1, 1) }));
    }

    [Test]
    public void Check_ValidOptionAndResult_NoDiagnostics()
    {
        var d = Check(@"
Option<i32> a = Option<i32>.Some(10)
let b = Option<i32>.None()
Result<i32, ParseError> r = Result<i32, ParseError>.Ok(100)
let e = Result<i32, ParseError>.Err(ParseError.EmptyInput)
switch a {
    Some(v) => println(v + 1)
    None => println(0)
}
switch r {
    Result.Ok(v) => println(v)
    Result.Err(err) => {
        switch err {
            ParseError.EmptyInput => println(1)
            ParseError.BadDigit => println(2)
        }
    }
}", ParseError);

        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_PayloadBindingHasConcreteType_AndIsArmScoped()
    {
        var typed = Check("Option<u8> a = Option<u8>.Some(3)\nswitch a {\n Some(v) => { u8 w = v }\n None => {}\n}");
        var wrongType = Check("Option<u8> a = Option<u8>.Some(3)\nswitch a {\n Some(v) => { bool w = v }\n None => {}\n}");
        var leaked = Check("Option<i32> a = Option<i32>.Some(3)\nswitch a {\n Some(v) => {}\n None => { println(v) }\n}");

        Assert.That(typed.HasErrors, Is.False, Messages(typed));
        Assert.That(Count(wrongType, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(leaked, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_WrongGenericArity_IsReported()
    {
        var tooMany = Check("Option<i32, bool> a");
        var tooFew = Check("Result<i32> r");

        Assert.That(Count(tooMany, DiagnosticCodes.GenericArgumentMismatch), Is.EqualTo(1));
        Assert.That(Count(tooFew, DiagnosticCodes.GenericArgumentMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ConstructorErrors_AreReported()
    {
        var payload = Check("let a = Option<i32>.Some(true)");
        var noneArg = Check("let a = Option<i32>.None(5)");
        var someNoArg = Check("let a = Option<i32>.Some()");
        var unknown = Check("let a = Option<i32>.Maybe(1)");
        var errType = Check("let a = Result<i32, bool>.Err(5)", "");

        Assert.That(Count(payload, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(noneArg, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(someNoArg, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(unknown, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(errType, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_SwitchErrors_AreReported()
    {
        var missingNone = Check("Option<i32> a = Option<i32>.None()\nswitch a { Some(v) => println(v) }");
        var missingErr = Check("Result<i32, bool> r = Result<i32, bool>.Ok(1)\nswitch r { Ok(v) => println(v) }");
        var duplicate = Check("Option<i32> a = Option<i32>.None()\nswitch a { Some(v) => println(v) Some(w) => println(w) None => {} }");
        var bindOnNone = Check("Option<i32> a = Option<i32>.None()\nswitch a { Some(v) => println(v) None(x) => println(x) }");
        var unknownArm = Check("Option<i32> a = Option<i32>.None()\nswitch a { Some(v) => println(v) None => {} Maybe(z) => {} }");

        Assert.That(Count(missingNone, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
        Assert.That(Count(missingErr, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
        Assert.That(Count(duplicate, DiagnosticCodes.DuplicateCase), Is.EqualTo(1));
        Assert.That(Count(bindOnNone, DiagnosticCodes.InvalidSwitch), Is.GreaterThan(0));
        Assert.That(unknownArm.HasErrors, Is.True);
    }

    [Test]
    public void Check_PreludeDoesNotOverrideUserDeclaredNames()
    {
        var d = Check("Option o = Option { x: 1 }", "struct Option { i32 x }");
        Assert.That(d.HasErrors, Is.False, Messages(d));
    }
}
