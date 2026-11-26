using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class SliceTests
{
    private static (ProgramNode, DiagnosticBag) Run(string body, string top = "")
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(top + "\nfunc main() {\n" + body + "\n}", diagnostics, "t.lumi");
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

    private static string Messages(DiagnosticBag d) => string.Join("\n", d.Diagnostics.Select(x => x.Message));

    [Test]
    public void Parse_SliceDeclarations_AreRecognised()
    {
        var (program, d) = Run(@"
*i32[] pointers
i32[3] arr = [1, 2, 3]
i32[] s = arr
Option<i32[]> opt = Option<i32[]>.Some(s)
let n = s.len");

        Assert.That(d.HasErrors, Is.False, Messages(d));
        var body = program.Functions.Single(f => f.Name == "main").Body;
        Assert.That(((VariableDeclarationNode)body[0]).Type, Is.EqualTo("*i32[]"));
        Assert.That(((VariableDeclarationNode)body[2]).Type, Is.EqualTo("i32[]"));
        Assert.That(((VariableDeclarationNode)body[3]).Type, Is.EqualTo("Option__i32_arr"));
    }

    [Test]
    public void Parse_IndexAssignment_IsNotADeclaration()
    {
        var (program, d) = Run("i32[3] a = [1, 2, 3]\na[0] = 5\na[1] = a[2]");

        Assert.That(d.HasErrors, Is.False, Messages(d));
        var body = program.Functions.Single(f => f.Name == "main").Body;
        Assert.That(body[1], Is.InstanceOf<TargetAssignmentStatementNode>());
        Assert.That(body[2], Is.InstanceOf<TargetAssignmentStatementNode>());
    }

    [Test]
    public void Check_SlicesInParametersReturnsAndFields()
    {
        var (_, d) = Run(@"
i32[3] arr = [1, 2, 3]
Holder h = Holder { items: arr }
println(first(h.items))
println(first([9, 8]))
let s = tail(arr)", @"
struct Holder { pub i32[] items }
func first(i32[] s) -> i32 { return s.len as i32 }
func tail(i32[3] a) -> i32[] { return a }");

        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_Len_IsI64_AndReadOnly()
    {
        var (_, ok) = Run("i32[2] a = [1, 2]\ni32[] s = a\ni64 x = s.len\ni64 y = a.len");
        var (_, assign) = Run("i32[2] a = [1, 2]\ni32[] s = a\ns.len = 5");
        var (_, wrong) = Run("i32[2] a = [1, 2]\ni32 x = a.len");

        Assert.That(ok.HasErrors, Is.False, Messages(ok));
        Assert.That(Count(assign, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(wrong, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Get_ReturnsOptionOfElement_AndBindsPayload()
    {
        var (_, d) = Run(@"
i32[] s = [4, 5]
switch s.get(1) {
    Some(v) => { i32 w = v }
    None => println(0)
}
Option<i32> o = s.get(0)
u8[2] bytes = [1, 2]
Option<u8> b = bytes.get(0)", "");

        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_Get_Errors()
    {
        var (_, nonInteger) = Run("i32[] s = [1]\nlet r = s.get(true)");
        var (_, noArg) = Run("i32[] s = [1]\nlet r = s.get()");
        var (_, other) = Run("i32[] s = [1]\nlet r = s.pop()");
        var (_, wrongOption) = Run("i32[] s = [1]\nOption<bool> r = s.get(0)");
        var (_, nonExhaustive) = Run("i32[] s = [1]\nswitch s.get(0) { Some(v) => println(v) }");

        Assert.That(Count(nonInteger, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(noArg, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(other, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(wrongOption, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(nonExhaustive, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
    }

    [Test]
    public void Check_ArrayToSliceConversion_RequiresSameElementType()
    {
        var (_, d) = Run("i64[2] a = [1, 2]\ni32[] s = a");
        Assert.That(Count(d, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_SliceIndexing_ReadsAndWritesElements()
    {
        var (_, ok) = Run("i32[3] a = [1, 2, 3]\ni32[] s = a\ni32 x = s[1]\ns[0] = x + 1\ns[2] = s[0]\ni64 i = 1\ni32 y = s[i]");
        var (_, wrongType) = Run("i32[] s = [1]\nbool b = s[0]");
        var (_, badIndex) = Run("i32[] s = [1]\nlet r = s[true]");
        var (_, negative) = Run("i32[] s = [1]\nlet r = s[-1]");
        var (_, nested) = Run("i32[2] a = [1, 2]\ni32[] s = a\nlet r = s[0..2][1]");

        Assert.That(ok.HasErrors, Is.False, Messages(ok));
        Assert.That(Count(wrongType, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(badIndex, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(negative, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(nested.HasErrors, Is.False, Messages(nested));
    }

    [Test]
    public void Check_ForIn_OverSlices()
    {
        var (_, ok) = Run("i32[3] a = [1, 2, 3]\ni32[] s = a\nfor x in s { i32 y = x }\nfor x in s[1..] { println(x) }\nfor x in [4, 5] { println(x) }");
        var (_, wrongElement) = Run("i32[] s = [1]\nfor x in s { bool b = x }");
        var (_, notIterable) = Run("i32 n = 1\nfor x in n { println(x) }");

        Assert.That(ok.HasErrors, Is.False, Messages(ok));
        Assert.That(Count(wrongElement, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(notIterable, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }
}
