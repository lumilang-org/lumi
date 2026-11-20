using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.CodeGenerator;

const string outputFile = "lumi.o";

static void PrintDiagnostics(DiagnosticBag diagnostics)
{
    foreach (var diagnostic in diagnostics.Diagnostics)
    {
        Console.Error.WriteLine(DiagnosticFormatter.Format(diagnostic));
    }
}

var sourceCodePath = args.Length > 0
    ? args[0]
    : Path.Combine(
        Directory.GetCurrentDirectory(),
        "main.lumi");

var lumiCode = File.ReadAllText(sourceCodePath);

Console.WriteLine("--- Initialising Lumi Lexer ---");

var diagnostics = new DiagnosticBag();

var lexer = new Lexer(lumiCode);
List<Token> tokens = [];

Token token;

do
{
    token = lexer.NextToken();
    tokens.Add(token);

    Console.WriteLine(
        $"{token.Type,-20} {token.Value,-12} " +
        $"{token.Span.Start.Line}:{token.Span.Start.Column} - " +
        $"{token.Span.End.Line}:{token.Span.End.Column}");
} while (token.Type != TokenType.EndOfFile);

var parser = new Parser(tokens, diagnostics);
var programAst = parser.ParseProgram();

if (diagnostics.HasErrors)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

var checker = new TypeChecker(diagnostics);
checker.CheckProgram(programAst);

if (diagnostics.HasErrors)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

Console.WriteLine("\n--- Emitting Native LLVM IR ---");

var generator = new Codegen("LumiModule", checker.SemanticModel);
generator.GenerateProgram(programAst);

generator.DumpModuleToConsole();

var outputDirectory = Path.Combine(
    Directory.GetCurrentDirectory(),
    "output");

Directory.CreateDirectory(outputDirectory);

var objectFilePath = Path.Combine(outputDirectory, outputFile);

generator.CompileToNativeObject(objectFilePath);

return 0;
