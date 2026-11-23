using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class TypeCheckerTests
{
    private (ProgramNode, SemanticModel, DiagnosticBag) Check(string source)
    {
        var lexer = new Lexer(source);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        var diagnostics = new DiagnosticBag();
        var parser = new Parser(tokens, diagnostics, "test.lumi");
        var program = parser.ParseProgram();

        var typeChecker = new TypeChecker(diagnostics);
        typeChecker.CheckProgram(program);

        return (program, typeChecker.SemanticModel, diagnostics);
    }

    [Test]
    public void Check_VariableDeclaration_TypeMismatch()
    {
        var source = "func main() -> void { i32 x = true }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Check_Array_InvalidLength()
    {
        // Zero length array is not allowed (LUMI1011)
        var source = "func main() -> void { i32[0] arr }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidArrayLength), Is.True);
    }

    [Test]
    public void Check_Array_ConstantIndexOutOfBounds()
    {
        var source = "func main() -> void { i32[2] arr = [1, 2] i32 x = arr[2] }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.IndexOutOfBounds), Is.True);
    }

    [Test]
    public void Check_Struct_FieldAccess_Valid()
    {
        var source = "struct Point { i32 x } func main() -> void { Point p = Point { x: 10 } i32 x = p.x }";
        var (_, semanticModel, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_Struct_MissingField()
    {
        var source = "struct Point { i32 x } func main() -> void { Point p = Point { x: 10 } i32 y = p.y }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
    }

    [Test]
    public void Check_FunctionCall_StructArgument_Valid()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func add(Vec2 v) -> i32 { return v.x + v.y }
        func main() -> void {
            Vec2 myVec = Vec2 { x: 10, y: 20 }
            i32 result = add(myVec)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_FunctionCall_ArrayArgument_Valid()
    {
        var source = @"
        func sum(i32[2] arr) -> i32 { return arr[0] + arr[1] }
        func main() -> void {
            i32[2] myArr = [10, 20]
            i32 result = sum(myArr)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_ReturnStruct_Valid()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func createVec(i32 x, i32 y) -> Vec2 {
            return Vec2 { x: x, y: y }
        }
        func main() -> void {
            Vec2 v = createVec(1, 2)
        }";
        var (_, _, diagnostics) = Check(source);
        if (diagnostics.HasErrors)
        {
            foreach (var d in diagnostics.Diagnostics)
            {
                throw new Exception($"[DEBUG_LOG] {d.Code}: {d.Message}");
            }
        }

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_ReturnArray_Valid()
    {
        var source = @"
        func createArr() -> i32[2] {
            return [1, 2]
        }
        func main() -> void {
            i32[2] a = createArr()
        }";
        var (_, _, diagnostics) = Check(source);
        if (diagnostics.HasErrors)
        {
            foreach (var d in diagnostics.Diagnostics)
            {
                Console.WriteLine($"[DEBUG_LOG] {d.Code}: {d.Message}");
            }
        }

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_FunctionCall_ArrayLengthMismatch()
    {
        var source = @"
        func sum(i32[3] arr) -> i32 { return arr[0] + arr[1] }
        func main() -> void {
            i32[2] myArr = [10, 20]
            i32 result = sum(myArr)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.TypeMismatch), Is.True);
    }
}
