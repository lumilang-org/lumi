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
}
