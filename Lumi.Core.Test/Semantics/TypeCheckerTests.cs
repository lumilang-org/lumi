using Lumi.Core.Ast;
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
        var parser = new Parser(tokens, diagnostics);
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
}
