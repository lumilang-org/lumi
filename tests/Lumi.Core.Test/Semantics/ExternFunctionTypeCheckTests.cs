using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class ExternFunctionTypeCheckTests
{
    private static (ProgramNode Program, SemanticModel Model, DiagnosticBag Diagnostics) Check(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics, "test.lumi").Tokenize();
        var program = new Parser(tokens, diagnostics, "test.lumi").ParseProgram();

        var checker = new TypeChecker(diagnostics);
        checker.CheckProgram(program);

        return (program, checker.SemanticModel, diagnostics);
    }

    private static int CountCode(DiagnosticBag diagnostics, string code)
    {
        return diagnostics.Diagnostics.Count(d => d.Code == code);
    }

    [Test]
    public void Check_ExternFunction_IsCallableLikeAnyFunction()
    {
        var (_, _, diagnostics) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void {
    i32 a = abs(5)
    i32 b = abs(a) + 1
    println(abs(-3))
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
    }

    [Test]
    public void Check_ExternFunction_RecordsResolvedReturnTypeAndSymbolName()
    {
        var (program, model, diagnostics) = Check(@"
extern func strlen(string s) -> usize
func main() -> void { }");

        Assert.That(diagnostics.HasErrors, Is.False);
        var node = program.ExternFunctions[0];
        Assert.That(model.GetType(node), Is.EqualTo("usize"));
        Assert.That(model.GetMangledName(node), Is.EqualTo("strlen"));
    }

    [Test]
    public void Check_ExternFunction_IsRegisteredUnderItsModuleMangledName()
    {
        var (program, model, _) = Check(@"
module std::c
extern func abs(i32 n) -> i32
extern func copy(*u8 dst, usize count) -> *u8");

        Assert.That(model.GetMangledName(program.ExternFunctions[0]), Is.EqualTo("std_c_abs"));
        Assert.That(model.Functions.TryGet("std_c_abs", out var abs), Is.True);
        Assert.That(abs.ParameterTypes, Is.EqualTo(new[] { "i32" }));
        Assert.That(abs.ReturnType, Is.EqualTo("i32"));

        Assert.That(model.Functions.TryGet("std_c_copy", out var copy), Is.True);
        Assert.That(copy.ParameterTypes, Is.EqualTo(new[] { "*u8", "usize" }));
        Assert.That(copy.ReturnType, Is.EqualTo("*u8"));
    }

    [Test]
    public void Check_ExternFunction_AcceptsEveryKindOfSignatureType()
    {
        var (_, _, diagnostics) = Check(@"
struct Point { i32 x i32 y }
extern func a(i8 v, u64 w, usize n, f32 f, bool b, char c, string s) -> f64
extern func b(*i32 p, **u8 pp, *Point q, *(i32[3]) arr) -> *Point
extern func c(Point p) -> Point
extern func d() -> void");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
    }

    [Test]
    public void Check_ExternFunction_CanBeCalledBeforeItsDeclaration()
    {
        var (_, _, diagnostics) = Check(@"
func main() -> void { i32 a = abs(1) }
extern func abs(i32 n) -> i32");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
    }

    [Test]
    public void Check_ExternFunction_ArgumentTypesAreChecked()
    {
        var (_, _, wrongType) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void { i32 a = abs(true) }");
        var (_, _, wrongWidth) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void { i64 v = 1 i32 a = abs(v) }");
        var (_, _, pointer) = Check(@"
extern func store(*i32 p) -> void
func main() -> void { i64 v = 1 store(&v) }");

        Assert.That(CountCode(wrongType, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(wrongWidth, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(pointer, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ExternFunction_ArgumentCountIsChecked()
    {
        var (_, _, tooFew) = Check(@"
extern func add(i32 a, i32 b) -> i32
func main() -> void { i32 r = add(1) }");
        var (_, _, tooMany) = Check(@"
extern func add(i32 a, i32 b) -> i32
func main() -> void { i32 r = add(1, 2, 3) }");

        Assert.That(CountCode(tooFew, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(CountCode(tooMany, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ExternFunction_ReturnTypeIsUsedByTheCaller()
    {
        var (_, _, mismatch) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void { bool b = abs(1) }");
        var (_, _, pointer) = Check(@"
extern func alloc(usize n) -> *u8
func main() -> void {
    *u8 p = alloc(8)
    *i32 q = alloc(8)
}");

        Assert.That(CountCode(mismatch, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(pointer, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_VoidExternFunction_IsAStatementButNotAValue()
    {
        var (_, _, statement) = Check(@"
extern func flush() -> void
func main() -> void { flush() }");
        var (_, _, value) = Check(@"
extern func flush() -> void
func main() -> void { i32 x = flush() }");

        Assert.That(statement.HasErrors, Is.False);
        Assert.That(value.HasErrors, Is.True);
    }

    [Test]
    public void Check_ExternFunction_UndeclaredCallsStillFail()
    {
        var (_, _, diagnostics) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void { i32 a = labs(1) }");

        Assert.That(CountCode(diagnostics, DiagnosticCodes.UndefinedFunction), Is.EqualTo(1));
    }

    [Test]
    public void Check_ExternFunction_DuplicateDeclarations_AreRejected()
    {
        var (_, _, twice) = Check(@"
extern func abs(i32 n) -> i32
extern func abs(i32 n) -> i32");
        var (_, _, clash) = Check(@"
extern func helper() -> void
func helper() -> void { }");
        var (_, _, clashReversed) = Check(@"
func helper() -> void { }
extern func helper() -> void");

        foreach (var diagnostics in new[] { twice, clash, clashReversed })
        {
            Assert.That(CountCode(diagnostics, DiagnosticCodes.DuplicateFunction), Is.EqualTo(1));
        }
    }

    [Test]
    public void Check_ExternFunction_UnknownTypesInTheSignature_AreRejected()
    {
        var (_, _, parameter) = Check("extern func f(Missing m) -> void");
        var (_, _, returned) = Check("extern func f() -> Missing");
        var (_, _, pointee) = Check("extern func f(*Missing m) -> void");

        Assert.That(CountCode(parameter, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(returned, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(pointee, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ExternFunction_DiagnosticsAreFileAwareWithSpans()
    {
        var (program, _, diagnostics) = Check("extern func f(Missing m) -> void");

        var diagnostic = diagnostics.Diagnostics.Single();
        Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
        Assert.That(diagnostic.Message, Does.Contain("Missing"));
        Assert.That(diagnostic.Span, Is.EqualTo(program.ExternFunctions[0].Parameters[0].Span));
    }

    [Test]
    public void Check_ExternFunction_ParametersAreNotVariablesOfTheCaller()
    {
        var (_, _, diagnostics) = Check(@"
extern func abs(i32 n) -> i32
func main() -> void { i32 a = n }");

        Assert.That(CountCode(diagnostics, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_ExternFunction_RecordsResolvedParameterTypesForCodegen()
    {
        var (program, model, diagnostics) = Check(@"
struct Point { i32 x }
extern func f(byte b, *Point p, string s) -> void");

        Assert.That(diagnostics.HasErrors, Is.False);
        var parameters = program.ExternFunctions[0].Parameters;
        Assert.That(parameters.Select(p => model.GetType(p)), Is.EqualTo(new[] { "u8", "*Point", "string" }));
    }
}
