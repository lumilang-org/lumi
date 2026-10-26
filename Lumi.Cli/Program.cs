using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.CodeGenerator;

const string outputFile = "lumi.o";

var lumiCode = @"
            func main() -> void {
                print(5)
                print(56789)

                u16 a = 30
                u16 b = 10

                print(a + b)

                i32 variable = -200
                i32 variable2 = -300              
                println(variable + variable2)

                char letter = 'A'
                println(letter)
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