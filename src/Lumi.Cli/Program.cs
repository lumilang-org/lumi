using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
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

Console.WriteLine("--- Initialising Lumi Lexer ---");

var diagnostics = new DiagnosticBag();
var loader = new ModuleLoader(diagnostics);
var modules = loader.Load(sourceCodePath);

if (diagnostics.HasErrors)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

var checker = new TypeChecker(diagnostics);
checker.CheckCompilationUnit(modules);

if (diagnostics.HasErrors)
{
    PrintDiagnostics(diagnostics);
    return 1;
}

Console.WriteLine("\n--- Emitting Native LLVM IR ---");

var generator = new Codegen("LumiModule", checker.SemanticModel);
generator.GenerateCompilationUnit(modules);

generator.DumpModuleToConsole();

var outputDirectory = Path.Combine(
    Directory.GetCurrentDirectory(),
    "output");

Directory.CreateDirectory(outputDirectory);

var objectFilePath = Path.Combine(outputDirectory, outputFile);

generator.CompileToNativeObject(objectFilePath);

return 0;
