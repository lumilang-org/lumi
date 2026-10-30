using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.CodeGenerator;

const string outputFile = "lumi.o";

var sourceCodePath = Path.Combine(Directory.GetCurrentDirectory(), "main.lumi");
var lumiCode = File.ReadAllText(sourceCodePath);

Console.WriteLine("--- Initialising Lumi Lexer ---");

var lexer = new Lexer(lumiCode);

Token token;
List<Token> tokens = [];

do
{
    token = lexer.NextToken();
    tokens.Add(token);
    Console.WriteLine(
        $"{token.Type,-20} {token.Value,-12} " +
        $"{token.Span.Start.Line}:{token.Span.Start.Column} - " +
        $"{token.Span.End.Line}:{token.Span.End.Column}");
} while (token.Type != TokenType.EndOfFile);

var parser = new Parser(tokens);
var programAst = parser.ParseProgram();

foreach (var function in programAst.Functions)
{
    Console.WriteLine(
        $"Parsed function: {function.Name}, " +
        $"Parameters: {function.Parameters.Count}, " +
        $"Return type: {function.ReturnType}");
}

var checker = new TypeChecker();
checker.CheckProgram(programAst);

Console.WriteLine("\n--- Emitting Native LLVM IR ---");
var generator = new Codegen("LumiModule", checker.SemanticModel);
generator.GenerateProgram(programAst);

generator.DumpModuleToConsole();

var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output");

Directory.CreateDirectory(outputDirectory);

var objectFilePath = Path.Combine(outputDirectory, outputFile);

generator.CompileToNativeObject(objectFilePath);
