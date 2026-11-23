using System.Diagnostics;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.LLVM.CodeGenerator;

namespace Lumi.LLVM.Test.CodeGenerator;

/// <summary>Compiles Lumi source to LLVM IR and runs it with lli (when installed).</summary>
internal static class CompilerHarness
{
    internal static readonly string? LliPath = FindLli();

    internal static string? FindLli()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "lli"))
            .Append("/opt/homebrew/opt/llvm/bin/lli");

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Compiles main.lumi (plus optional sibling modules) and returns the LLVM IR.</summary>
    internal static string CompileToIr(string main, params (string Name, string Source)[] siblings)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            foreach (var (name, source) in siblings)
            {
                File.WriteAllText(Path.Combine(directory, name), source);
            }

            var mainPath = Path.Combine(directory, "main.lumi");
            File.WriteAllText(mainPath, main);

            var diagnostics = new DiagnosticBag();
            var modules = new ModuleLoader(diagnostics).Load(mainPath);
            var checker = new TypeChecker(diagnostics);

            if (!diagnostics.HasErrors)
            {
                checker.CheckCompilationUnit(modules);
            }

            if (diagnostics.HasErrors)
            {
                throw new Exception("Compilation failed: " + string.Join(
                    "; ", diagnostics.Diagnostics.Select(DiagnosticFormatter.Format)));
            }

            // GenerateCompilationUnit throws if the LLVM verifier rejects the module.
            var codegen = new Codegen("test_module", checker.SemanticModel);
            codegen.GenerateCompilationUnit(modules);
            return codegen.GetModuleIr();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Runs the IR with lli. Returns null when lli is not installed.</summary>
    internal static string? Run(string ir)
    {
        if (LliPath is null)
        {
            return null;
        }

        var irPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ll");
        File.WriteAllText(irPath, ir);

        try
        {
            using var process = Process.Start(new ProcessStartInfo(LliPath, irPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        finally
        {
            File.Delete(irPath);
        }
    }

    internal static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal);
             i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
