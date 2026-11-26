using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class TupleTests
{
    private static (ProgramNode, DiagnosticBag) Check(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics, "t.lumi");
        var tokens = new List<Token>();
        Token token;
        do { token = lexer.NextToken(); tokens.Add(token); } while (token.Type != TokenType.EndOfFile);

        var program = new Parser(tokens, diagnostics, "t.lumi").ParseProgram();
        new TypeChecker(diagnostics).CheckProgram(program);
        return (program, diagnostics);
    }

    private static DiagnosticBag Body(string body) =>
        Check("func pair() -> (i32, bool) { return (1, true) }\nfunc main() -> void {\n" + body + "\n}").Item2;

    private static string Messages(DiagnosticBag d) => string.Join("\n", d.Diagnostics.Select(x => x.Message));
    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    [Test]
    public void Parse_TupleTypes_Literals_AndDestructuring()
    {
        var (program, diagnostics) = Check(@"
struct S { (i32, i32) p }
func f((i32, i32) a) -> (i32, (bool, u8)) { return (1, (true, 2)) }
func main() -> void {
    (i32, i32) t = (1, 2)
    let (a, (b, _)) = f(t)
}");

        Assert.That(diagnostics.HasErrors, Is.False, Messages(diagnostics));
        Assert.That(program.Structs[0].Fields[0].Type, Is.EqualTo("(i32, i32)"));
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo("(i32, (bool, u8))"));
        Assert.That(program.Functions[1].Body[0], Is.InstanceOf<VariableDeclarationNode>());
        Assert.That(program.Functions[1].Body[1], Is.InstanceOf<TupleDestructuringNode>());
    }

    [Test]
    public void Parse_SingleElementTupleType_IsRejected_ButGroupingStaysAnExpression()
    {
        var (_, bad) = Check("func main() -> void { (i32, i32) ok = (1, 2) }");
        var (program, grouped) = Check("func main() -> void { i32 x = (1 + 2) * 3 }");
        var tupleType = Check("func f() -> (i32) { return 1 }").Item2;

        Assert.That(bad.HasErrors, Is.False);
        Assert.That(grouped.HasErrors, Is.False, Messages(grouped));
        Assert.That(((VariableDeclarationNode)program.Functions[0].Body[0]).Initializer, Is.InstanceOf<BinaryExpressionNode>());
        Assert.That(tupleType.HasErrors, Is.True);
    }

    [Test]
    public void Check_Literals_ReturnsAndInference()
    {
        var d = Body(@"
let t = (1, true)
(i64, u8) w = (7, 8)
let (n, b) = pair()
i32 x = n
bool y = b");
        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_Destructuring_BindsElementTypes()
    {
        Assert.That(Count(Body("let (n, b) = pair()\nbool wrong = n"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(Body("let (n, b) = pair()\ni32 wrong = b"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Body("let (_, b) = pair()\nbool ok = b").HasErrors, Is.False);
        Assert.That(Count(Body("let (_, b) = pair()\ni32 x = _"), DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_Destructuring_Diagnostics()
    {
        Assert.That(Count(Body("let (a, b, c) = pair()"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(Body("let (a) = pair()"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(Body("let (a, b) = 5"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(Body("let (a, a) = pair()"), DiagnosticCodes.DuplicateVariable), Is.EqualTo(1));
        Assert.That(Count(Body("i32 a = 1\nlet (a, b) = pair()"), DiagnosticCodes.DuplicateVariable), Is.EqualTo(1));
        Assert.That(Count(Body("let (a, (b, c)) = pair()"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(Body("let (a, b) = pair()\nlet (a, c) = pair()"), DiagnosticCodes.DuplicateVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_ReturnAndAssignment_MustMatchTupleType()
    {
        var (_, wrongArity) = Check("func f() -> (i32, i32) { return (1, 2, 3) }");
        var (_, wrongType) = Check("func f() -> (i32, i32) { return (1, true) }");
        var (_, notTuple) = Check("func f() -> (i32, i32) { return 1 }");
        var (_, assign) = Check("func main() -> void { (i32, bool) t = (1, 2) }");
        var (_, ok) = Check("func f() -> (i64, u8) { return (1, 2) }");

        Assert.That(Count(wrongArity, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(wrongType, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(notTuple, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(assign, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(ok.HasErrors, Is.False, Messages(ok));
    }

    [Test]
    public void Check_TupleTypes_ValidateElements_AndArguments()
    {
        var (_, unknown) = Check("func f((i32, Missing) p) -> void { }");
        var (_, argMismatch) = Check("func f((i32, i32) p) -> void { }\nfunc main() -> void { f((1, true)) }");
        var (_, argOk) = Check("func f((i32, i32) p) -> void { }\nfunc main() -> void { f((1, 2)) }");

        Assert.That(Count(unknown, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(argMismatch, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(argOk.HasErrors, Is.False, Messages(argOk));
    }

    [Test]
    public void TupleTypes_Helpers_SplitOnlyTopLevelCommas()
    {
        Assert.That(TupleTypes.Elements("(i32, (bool, u8), i32[2])"), Is.EqualTo(new[] { "i32", "(bool, u8)", "i32[2]" }));
        Assert.That(PointerTypes.Pointee(PointerTypes.To("(i32, i32)")), Is.EqualTo("(i32, i32)"));
        Assert.That(PointerTypes.Pointee(PointerTypes.To("i32[3]")), Is.EqualTo("i32[3]"));
        Assert.That(PointerTypes.Pointee(PointerTypes.To("(i32, i32)[3]")), Is.EqualTo("(i32, i32)[3]"));
    }

    [Test]
    public void Check_ElementAccess_TypesAndDiagnostics()
    {
        Assert.That(Body("let t = (1, true)\ni32 a = t.0\nbool b = t.1\nt.0 = 5\nt.1 = false").HasErrors, Is.False);
        Assert.That(Body("let t = (1, (true, 2))\nbool b = t.1.0\ni32 c = t.1.1").HasErrors, Is.False);
        Assert.That(Body("let t = (1, true)\n*(i32, bool) p = &t\ni32 a = p.0").HasErrors, Is.False);
        Assert.That(Body("i32 a = pair().1").HasErrors, Is.True); // bool is not i32
        Assert.That(Body("bool a = pair().1").HasErrors, Is.False);

        Assert.That(Count(Body("let t = (1, true)\ni32 a = t.2"), DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(Count(Body("let t = (1, true)\ni32 a = t.x"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(Body("let t = (1, true)\nt.0 = true"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(Body("i32 n = 5\ni32 a = n.0"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
    }

    [Test]
    public void Parse_ElementAccess_SplitsChainedIndices()
    {
        var (program, diagnostics) = Check("func main() -> void { let t = (1, (2, 3))\nlet v = t.1.0 }");
        var decl = (VariableDeclarationNode)program.Functions[0].Body[1];
        var outer = (MemberAccessNode)decl.Initializer!;

        Assert.That(diagnostics.HasErrors, Is.False, Messages(diagnostics));
        Assert.That(outer.MemberName, Is.EqualTo("0"));
        Assert.That(((MemberAccessNode)outer.Target).MemberName, Is.EqualTo("1"));
    }

    [Test]
    public void Lex_IndexFollowedByMemberName_IsNotADecimalPoint()
    {
        var (_, diagnostics) = Check(@"
struct V { i32 x }
func f() -> (string, V) { return (""a"", V { x: 3 }) }
func main() -> void {
    let t = f()
    i32 a = t.1.x
    let n = 1.5
}");
        Assert.That(diagnostics.HasErrors, Is.False, Messages(diagnostics));
    }

    [Test]
    public void Parse_FunctionWithoutArrow_ReturnsVoid()
    {
        var (program, diagnostics) = Check(@"
extern func abs(i32 n) -> i32
extern func tick()
struct S {
    i32 v
    func bump(*S self) { self.v = self.v + 1 }
}
func helper() { }
func main() { helper() }");

        Assert.That(diagnostics.HasErrors, Is.False, Messages(diagnostics));
        Assert.That(program.Functions.Select(f => f.ReturnType), Is.All.EqualTo("void"));
        Assert.That(program.Structs[0].Methods[0].ReturnType, Is.EqualTo("void"));
        Assert.That(program.ExternFunctions[1].ReturnType, Is.EqualTo("void"));

        var (_, bad) = Check("func f() { return 1 }");
        Assert.That(bad.HasErrors, Is.True);
    }
}
