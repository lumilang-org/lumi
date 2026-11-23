using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.CodeGenerator;

namespace Lumi.LLVM.Test.CodeGenerator;

public class CodegenTests
{
    private string GenerateIR(string source)
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

        if (diagnostics.HasErrors)
        {
            throw new Exception("Type checking failed: " +
                                string.Join(", ", diagnostics.Diagnostics.Select(d => d.Message)));
        }

        var codegen = new Codegen("test_module", typeChecker.SemanticModel);
        codegen.GenerateProgram(program);

        // We can't easily get the IR as a string without a bit more work,
        // but we can verify it doesn't crash and we can potentially use VerifyModule.
        // For the sake of this task, let's see if we can get the module's IR string.
        // In LLVMSharp, this is often module.PrintToString()
        return ""; // Placeholder
    }

    [Fact]
    public void Codegen_BasicProgram_DoesNotCrash()
    {
        var source = "func main() -> void { i32 x = 10 }";
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

        var codegen = new Codegen("test_module", typeChecker.SemanticModel);
        codegen.GenerateProgram(program);

        // If we reach here, it didn't crash.
    }

    [Fact]
    public void Codegen_ArrayAndStruct_DoesNotCrash()
    {
        var source = @"
        struct Point { i32 x i32 y }
        func main() -> void {
            Point[2] points = [Point { x: 1, y: 2 }, Point { x: 3, y: 4 }]
            points[0].x = 10
        }";
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

        var codegen = new Codegen("test_module", typeChecker.SemanticModel);
        codegen.GenerateProgram(program);
    }

    [Fact]
    public void Codegen_PassStructAndArray_DoesNotCrash()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func process(Vec2 v, i32[2] arr) -> Vec2 {
            return Vec2 { x: v.x + arr[0], y: v.y + arr[1] }
        }
        func main() -> void {
            Vec2 v = Vec2 { x: 1, y: 2 }
            i32[2] a = [10, 20]
            Vec2 result = process(v, a)
        }";
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

        var codegen = new Codegen("test_module", typeChecker.SemanticModel);
        codegen.GenerateProgram(program);
    }

    [Fact]
    public void Codegen_StructLiteralAsArgument_DoesNotCrash()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func getX(Vec2 v) -> i32 { return v.x }
        func main() -> void {
            i32 x = getX(Vec2 { x: 10, y: 20 })
        }";

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

        var codegen = new Codegen("test_module", typeChecker.SemanticModel);
        codegen.GenerateProgram(program);
    }
}
