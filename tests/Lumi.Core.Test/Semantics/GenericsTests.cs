using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class GenericsTests
{
    private static (Lumi.Core.Ast.Declarations.ProgramNode, DiagnosticBag) Check(string source)
    {
        var diagnostics = new DiagnosticBag();
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
        return (program, diagnostics);
    }

    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    private const string Vec2 = "struct Vec2 { pub i32 x pub i32 y }\n";

    [Test]
    public void Parse_GenericDeclarations_KeepTemplatesApart()
    {
        var (program, d) = Check(Vec2 + @"
struct Box<T> { pub T value }
struct Mover<T> where T : Vec2 { pub T pos }
func id<T>(T v) -> T { return v }
func step<T, U>(T v, U u) -> T where T : Vec2, U : i32 { return v }
func main() {}");

        Assert.That(d.HasErrors, Is.False, string.Join("\n", d.Diagnostics.Select(x => x.Message)));
        Assert.That(program.GenericStructs.Select(s => s.Name), Is.EqualTo(new[] { "Box", "Mover" }));
        Assert.That(program.GenericFunctions.Select(f => f.Name), Is.EqualTo(new[] { "id", "step" }));
        Assert.That(program.GenericFunctions[1].Constraints, Has.Count.EqualTo(2));
        Assert.That(program.GenericStructs[1].Constraints[0].BoundType, Is.EqualTo("Vec2"));
        Assert.That(program.Structs.Select(s => s.Name), Is.EqualTo(new[] { "Vec2" }));
    }

    [Test]
    public void Instantiation_AddsSpecializationsOnce_WithMangledNames()
    {
        var (program, d) = Check(@"
struct Box<T> { pub T value }
func main() {
    Box<i32> a = Box<i32> { value: 1 }
    Box<i32> b = Box<i32> { value: 2 }
    Box<Box<i32>> c = Box<Box<i32>> { value: a }
}");

        Assert.That(d.HasErrors, Is.False, string.Join("\n", d.Diagnostics.Select(x => x.Message)));
        Assert.That(program.Structs.Select(s => s.Name), Is.EquivalentTo(new[] { "Box__i32", "Box__Box__i32" }));
    }

    [Test]
    public void Parse_EmptyAndDuplicateTypeParameters_AreRejected()
    {
        var (_, empty) = Check("func f<>() {}");
        var (_, duplicate) = Check("func f<T, T>() {}");

        Assert.That(Count(empty, DiagnosticCodes.InvalidTypeParameters), Is.EqualTo(1));
        Assert.That(Count(duplicate, DiagnosticCodes.InvalidTypeParameters), Is.EqualTo(1));
    }

    [Test]
    public void Parse_BadWhereClauses_AreRejected()
    {
        var (_, undeclared) = Check(Vec2 + "func f<T>(T v) where U : Vec2 {}");
        var (_, twice) = Check(Vec2 + "func f<T>(T v) where T : Vec2, T : i32 {}");
        var (_, unknown) = Check("func f<T>(T v) where T : Missing {}");

        Assert.That(Count(undeclared, DiagnosticCodes.InvalidTypeParameters), Is.EqualTo(1));
        Assert.That(Count(twice, DiagnosticCodes.InvalidTypeParameters), Is.EqualTo(1));
        Assert.That(Count(unknown, DiagnosticCodes.InvalidTypeParameters), Is.EqualTo(1));
    }

    [Test]
    public void Check_ConstraintViolation_IsReported()
    {
        var (program, d) = Check(Vec2 + @"
func step<T>(T v) -> T where T : Vec2 { return v }
func main() { let r = step<i32>(1) }");

        Assert.That(Count(d, DiagnosticCodes.ConstraintNotSatisfied), Is.EqualTo(1));
        Assert.That(program.Functions.Select(f => f.Name), Does.Not.Contain("step__i32"));
    }

    [Test]
    public void Check_TypeArgumentCountMismatch_IsReported()
    {
        var (_, d) = Check(@"
struct Box<T> { pub T value }
func main() { Box<i32, u8> b }");

        Assert.That(Count(d, DiagnosticCodes.GenericArgumentMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_TypeParameterOutsideItsDeclaration_IsUnknown()
    {
        var (_, d) = Check("func main() { T x }");
        Assert.That(d.HasErrors, Is.True);
    }

    [Test]
    public void Check_RunawayInstantiation_IsStopped()
    {
        var (_, d) = Check(@"
struct Grow<T> { pub Grow<Grow<T>> inner }
func main() { Grow<i32> g }");

        Assert.That(Count(d, DiagnosticCodes.GenericInstantiationTooDeep), Is.EqualTo(1));
    }

    [Test]
    public void Parse_LessThanStillComparesOnNonGenericNames()
    {
        var (_, d) = Check(@"
func id<T>(T v) -> T { return v }
func main() {
    i32 a = 1
    i32 b = 2
    if a < b { println(id<i32>(a)) }
}");

        Assert.That(d.HasErrors, Is.False, string.Join("\n", d.Diagnostics.Select(x => x.Message)));
    }
}
