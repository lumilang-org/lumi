using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.CodeGenerator;

const string outputFile = "lumi.o";

var lumiCode = @"
            func main() -> void {
                u32 a = 10
                u32 b = 5

                bool result = 10 / 5
                println(result)
            }
        ";

Console.WriteLine("--- Initialising Lumi Lexer ---");

var lexer = new Lexer(lumiCode);

Token token;
List<Token> tokens = new();

do
{
    token = lexer.NextToken();
    tokens.Add(token);
    Console.WriteLine($"Token: {token.Type} | Value: \"{token.Value}\"");
} while (token.Type != TokenType.EndOfFile);

var parser = new Parser(tokens);
var programAst = parser.ParseFunction();

var checker = new TypeChecker();
checker.CheckFunction(programAst);

Console.WriteLine("\n--- Emitting Native LLVM IR ---");
var generator = new Codegen("LumiModule", checker.SemanticModel);
generator.GenerateFunction(programAst);

generator.DumpModuleToConsole();

var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output");

Directory.CreateDirectory(outputDirectory);

var objectFilePath = Path.Combine(outputDirectory, outputFile);

generator.CompileToNativeObject(objectFilePath);