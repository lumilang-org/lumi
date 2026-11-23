using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Syntax;

[TestFixture]
public class ExternFunctionDeclarationTests
{
    private static (ProgramNode Program, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics, "test.lumi").Tokenize();
        var program = new Parser(tokens, diagnostics, "test.lumi").ParseProgram();

        return (program, diagnostics);
    }

    private static ExternFunctionDeclarationNode ParseSingle(string source)
    {
        var (program, diagnostics) = Parse(source);

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        Assert.That(program.ExternFunctions, Has.Count.EqualTo(1));
        return program.ExternFunctions[0];
    }

    [Test]
    public void Extern_IsAKeyword_ButLongerNamesAreIdentifiers()
    {
        Assert.That(new Lexer("extern", new DiagnosticBag(), "test.lumi").NextToken().Type,
            Is.EqualTo(TokenType.KeywordExtern));

        foreach (var name in new[] { "externs", "extern2", "my_extern", "Extern", "_extern" })
        {
            Assert.That(new Lexer(name, new DiagnosticBag(), "test.lumi").NextToken().Type,
                Is.EqualTo(TokenType.Identifier), name);
        }
    }

    [Test]
    public void Parse_ExternFunction_NameParametersAndReturnType()
    {
        var node = ParseSingle("extern func abs(i32 n) -> i32");

        Assert.That(node.Name, Is.EqualTo("abs"));
        Assert.That(node.ReturnType, Is.EqualTo("i32"));
        Assert.That(node.Parameters, Has.Count.EqualTo(1));
        Assert.That(node.Parameters[0].Type, Is.EqualTo("i32"));
        Assert.That(node.Parameters[0].Name, Is.EqualTo("n"));
    }

    [Test]
    public void Parse_ExternFunction_WithoutParametersReturningVoid()
    {
        var node = ParseSingle("extern func flush() -> void");

        Assert.That(node.Name, Is.EqualTo("flush"));
        Assert.That(node.Parameters, Is.Empty);
        Assert.That(node.ReturnType, Is.EqualTo("void"));
    }

    [Test]
    public void Parse_ExternFunction_WithManyParametersOfDifferentKinds()
    {
        var node = ParseSingle(
            "extern func copy(*u8 dst, *u8 src, usize count, string label, bool strict, Point p) -> *u8");

        Assert.That(node.Parameters.Select(p => p.Type),
            Is.EqualTo(new[] { "*u8", "*u8", "usize", "string", "bool", "Point" }));
        Assert.That(node.Parameters.Select(p => p.Name),
            Is.EqualTo(new[] { "dst", "src", "count", "label", "strict", "p" }));
        Assert.That(node.ReturnType, Is.EqualTo("*u8"));
    }

    [TestCase("i8")]
    [TestCase("u64")]
    [TestCase("usize")]
    [TestCase("f64")]
    [TestCase("char")]
    [TestCase("bool")]
    [TestCase("string")]
    [TestCase("**i32")]
    [TestCase("*(i32[3])")]
    [TestCase("Point")]
    public void Parse_ExternFunction_AcceptsEveryTypeKindAsReturnType(string returnType)
    {
        var node = ParseSingle($"extern func f() -> {returnType}");

        Assert.That(node.ReturnType, Is.EqualTo(returnType));
    }

    [Test]
    public void Parse_ExternFunction_SpanCoversTheWholeDeclaration()
    {
        var node = ParseSingle("extern func abs(i32 n) -> i32");

        Assert.That(node.Span.Start.Line, Is.EqualTo(1));
        Assert.That(node.Span.Start.Column, Is.EqualTo(1));
        Assert.That(node.Span.End.Line, Is.EqualTo(1));
        Assert.That(node.Span.End.Column, Is.EqualTo("extern func abs(i32 n) -> i32".Length + 1));
    }

    [Test]
    public void Parse_ExternFunction_SpanStopsBeforeTheNextDeclaration()
    {
        var (program, diagnostics) = Parse("extern func a() -> i32\nextern func b() -> void");

        Assert.That(diagnostics.HasErrors, Is.False);
        var first = program.ExternFunctions[0];
        Assert.That(first.Span.End.Line, Is.EqualTo(1));
        Assert.That(first.Span.End.Column, Is.EqualTo("extern func a() -> i32".Length + 1));
    }

    [Test]
    public void Parse_ExternFunction_ParameterSpansPointAtTheirDeclaration()
    {
        var node = ParseSingle("extern func add(i32 a, i32 b) -> i32");

        Assert.That(node.Parameters[0].Span.Start.Column, Is.EqualTo(17));
        Assert.That(node.Parameters[1].Span.Start.Column, Is.EqualTo(24));
    }

    [Test]
    public void Parse_ExternFunctions_AreKeptApartFromFunctionsAndStructs_InSourceOrder()
    {
        var (program, diagnostics) = Parse(@"
extern func first(i32 n) -> i32
struct Point { i32 x i32 y }
func helper() -> void { }
extern func second() -> void
func main() -> void { }
extern func third(*u8 p) -> usize");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        Assert.That(program.ExternFunctions.Select(f => f.Name), Is.EqualTo(new[] { "first", "second", "third" }));
        Assert.That(program.Functions.Select(f => f.Name), Is.EqualTo(new[] { "helper", "main" }));
        Assert.That(program.Structs.Select(s => s.Name), Is.EqualTo(new[] { "Point" }));
    }

    [Test]
    public void Parse_ExternFunction_FollowedByAFunctionOnTheSameLine()
    {
        var (program, diagnostics) = Parse("extern func abs(i32 n) -> i32 func main() -> void { }");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        Assert.That(program.ExternFunctions, Has.Count.EqualTo(1));
        Assert.That(program.Functions.Select(f => f.Name), Is.EqualTo(new[] { "main" }));
    }

    [Test]
    public void Parse_ExternFunction_AfterModuleAndImports()
    {
        var (program, diagnostics) = Parse(@"
module std::c
import std::string
extern func abs(i32 n) -> i32");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        Assert.That(program.Module!.PathSegments, Is.EqualTo(new[] { "std", "c" }));
        Assert.That(program.ExternFunctions.Select(f => f.Name), Is.EqualTo(new[] { "abs" }));
    }

    [Test]
    public void Parse_ProgramWithoutExterns_HasAnEmptyList()
    {
        var (program, _) = Parse("func main() -> void { }");

        Assert.That(program.ExternFunctions, Is.Empty);
    }

    [Test]
    public void Parse_ExternFunction_WithABody_IsAnError()
    {
        var (_, diagnostics) = Parse("extern func abs(i32 n) -> i32 { return n }");

        var diagnostic = diagnostics.Diagnostics.First(d => d.Code == DiagnosticCodes.UnexpectedToken);
        Assert.That(diagnostic.Message, Does.Contain("'abs'").And.Contain("cannot have a body"));
        Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
        Assert.That(diagnostic.Span!.Start.Column, Is.EqualTo(31));
    }

    [TestCase("extern abs(i32 n) -> i32", "missing 'func'")]
    [TestCase("extern func (i32 n) -> i32", "missing name")]
    [TestCase("extern func abs i32 n) -> i32", "missing '('")]
    [TestCase("extern func abs(i32 n -> i32", "missing ')'")]
    [TestCase("extern func abs(i32 n) i32", "missing '->'")]
    [TestCase("extern func abs(i32 n) ->", "missing return type")]
    [TestCase("extern func abs(i32 n) -> 5", "return type is not a type")]
    [TestCase("extern func abs(i32) -> i32", "parameter without a name")]
    public void Parse_MalformedExternFunction_ReportsAFileAwareSyntaxError(string source, string reason)
    {
        var (_, diagnostics) = Parse(source);

        Assert.That(diagnostics.HasErrors, Is.True, reason);
        Assert.That(diagnostics.Diagnostics.All(d => d.FileName == "test.lumi"), Is.True, reason);
        Assert.That(diagnostics.Diagnostics.All(d => d.Span is not null), Is.True, reason);
    }

    [Test]
    public void Parse_MalformedExternFunction_DoesNotHideLaterDeclarations()
    {
        var (program, diagnostics) = Parse("extern func broken(\nfunc main() -> void { }");

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(program.ExternFunctions, Is.Empty);
        Assert.That(program.Functions.Select(f => f.Name), Does.Contain("main"));
    }
}
