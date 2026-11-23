using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class ModuleLoader(DiagnosticBag diagnostics)
{
    private readonly HashSet<string> _inProgress = new();
    private readonly Dictionary<string, ProgramNode> _loadedModules = new();
    private readonly List<ProgramNode> _orderedModules = new();

    public IReadOnlyList<ProgramNode> Load(string entryFilePath)
    {
        var absolutePath = Path.GetFullPath(entryFilePath);
        LoadInternal(absolutePath);
        return _orderedModules;
    }

    private void LoadInternal(string filePath)
    {
        if (_loadedModules.ContainsKey(filePath))
        {
            return;
        }

        if (!_inProgress.Add(filePath))
        {
            diagnostics.Report(
                filePath,
                DiagnosticCodes.CircularImport,
                $"Circular dependency detected involving file: {filePath}");
            return;
        }

        string source;
        try
        {
            source = File.ReadAllText(filePath);
        }
        catch (Exception)
        {
            diagnostics.Report(
                filePath,
                DiagnosticCodes.ModuleNotFound,
                $"Could not read file: {filePath}");
            _inProgress.Remove(filePath);
            return;
        }

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, diagnostics, filePath);
        var program = parser.ParseProgram();

        foreach (var import in program.Imports)
        {
            var importedPath = ResolveImport(filePath, import);
            if (importedPath != null)
            {
                LoadInternal(importedPath);
            }
        }

        _inProgress.Remove(filePath);
        _loadedModules[filePath] = program;
        _orderedModules.Add(program);
    }

    private string? ResolveImport(string currentFilePath, ImportDeclarationNode import)
    {
        var directory = Path.GetDirectoryName(currentFilePath) ?? ".";

        // Try direct relative path
        var relativePath = Path.Combine(import.PathSegments.ToArray()) + ".lumi";
        var absolutePath = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (File.Exists(absolutePath))
        {
            return absolutePath;
        }

        // Try searching in subdirectories if it's a multi-segment path
        // e.g. std::math -> std/math.lumi
        if (import.PathSegments.Count > 1)
        {
            // Already handled by Path.Combine(import.PathSegments.ToArray())
        }

        // Try looking at the filename itself in the same directory
        var lastSegment = import.PathSegments[^1] + ".lumi";
        absolutePath = Path.GetFullPath(Path.Combine(directory, lastSegment));
        if (File.Exists(absolutePath))
        {
            return absolutePath;
        }

        diagnostics.Report(
            currentFilePath,
            DiagnosticCodes.ModuleNotFound,
            $"Could not find module '{string.Join("::", import.PathSegments)}'",
            import.Span);
        return null;
    }
}
