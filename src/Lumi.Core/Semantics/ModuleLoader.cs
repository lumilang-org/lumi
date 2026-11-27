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

        var lexer = new Lexer(source, diagnostics, filePath);
        var tokens = lexer.Tokenize();

        // Imported modules are loaded first so their generic names are known while parsing this
        // file (`Vec<i32> v` must parse as a type, not as comparisons).
        var importedGenerics = new List<string>();
        foreach (var import in ScanImports(tokens))
        {
            var importedPath = ResolveImport(filePath, import, report: false);
            if (importedPath is null)
            {
                continue;
            }

            LoadInternal(importedPath);

            if (_loadedModules.TryGetValue(importedPath, out var imported))
            {
                importedGenerics.AddRange(imported.GenericFunctions.Select(f => f.Name));
                importedGenerics.AddRange(imported.GenericStructs.Select(s => s.Name));
                importedGenerics.AddRange(imported.GenericEnums.Select(e => e.Name));
            }
        }

        var parser = new Parser(tokens, diagnostics, filePath, importedGenerics);
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

    // The `import a::b` lines at the top of a file, read straight from its tokens.
    private static List<ImportDeclarationNode> ScanImports(IReadOnlyList<Token> tokens)
    {
        var imports = new List<ImportDeclarationNode>();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type != TokenType.KeywordImport)
            {
                continue;
            }

            var path = new List<string>();
            var j = i + 1;

            while (j < tokens.Count && tokens[j].Type == TokenType.Identifier)
            {
                path.Add(tokens[j].Value);
                j++;

                if (j < tokens.Count && tokens[j].Type == TokenType.DoubleColon)
                {
                    j++;
                }
                else
                {
                    break;
                }
            }

            if (path.Count > 0)
            {
                imports.Add(new ImportDeclarationNode(path, tokens[i].Span));
            }
        }

        return imports;
    }

    private string? ResolveImport(string currentFilePath, ImportDeclarationNode import, bool report = true)
    {
        var directory = Path.GetDirectoryName(currentFilePath) ?? ".";

        // Try direct relative path
        var relativePath = Path.Combine(import.PathSegments.ToArray()) + ".lumi";
        var absolutePath = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (File.Exists(absolutePath))
        {
            return absolutePath;
        }

        // The standard library ships next to the compiler (copied to <OutDir>/std),
        // so std::string resolves regardless of the working directory.
        if (import.PathSegments[0] == "std")
        {
            var stdPath = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(stdPath))
            {
                return stdPath;
            }
        }

        // Try looking at the filename itself in the same directory
        var lastSegment = import.PathSegments[^1] + ".lumi";
        absolutePath = Path.GetFullPath(Path.Combine(directory, lastSegment));
        if (File.Exists(absolutePath))
        {
            return absolutePath;
        }

        if (report)
        {
            diagnostics.Report(
                currentFilePath,
                DiagnosticCodes.ModuleNotFound,
                $"Could not find module '{string.Join("::", import.PathSegments)}'",
                import.Span);
        }

        return null;
    }
}
