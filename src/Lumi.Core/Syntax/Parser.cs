using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser(
    IReadOnlyList<Token> tokens,
    DiagnosticBag diagnostics,
    string fileName,
    IEnumerable<string>? importedGenericNames = null)
{
    private int _position;

    // Second `>` of a `>>` token that closed two type-argument lists at once.
    private int _pendingCloseAngle;

    // Names declared as generic (`func name<T>`, `struct Name<T>`) in this file. Only these
    // names may be followed by `<T>` type arguments, so `a < b` stays a comparison.
    private HashSet<string> _genericNames = [];

    // Helper properties to read the current and upcoming tokens
    private Token Current => Peek(0);

    private Token Next => Peek(1);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        if (index >= tokens.Count)
        {
            return tokens[^1]; // Always return the final EOF token if out of range
        }

        return tokens[index];
    }

    private void Advance()
    {
        if (_position < tokens.Count - 1)
        {
            _position++;
        }
    }

    private Token Consume(TokenType type)
    {
        if (Current.Type != type)
        {
            Report(
                DiagnosticCodes.ExpectedToken,
                $"Syntax Error: Expected token {type}, got {Current.Type} ('{Current.Value}') at position {_position}",
                Current.Span);
            throw new ParseException();
        }

        var token = Current;
        Advance();

        return token;
    }

    public ProgramNode ParseProgram()
    {
        ModuleDeclarationNode? module = null;
        var imports = new List<ImportDeclarationNode>();
        var functions = new List<FunctionDeclarationNode>();
        var externFunctions = new List<ExternFunctionDeclarationNode>();
        var structs = new List<StructDeclarationNode>();
        var enums = new List<EnumDeclarationNode>();

        _genericNames = CollectGenericNames();
        var genericFunctions = new List<FunctionDeclarationNode>();
        var genericStructs = new List<StructDeclarationNode>();
        var genericEnums = new List<EnumDeclarationNode>();

        if (Current.Type == TokenType.KeywordModule)
        {
            module = ParseModuleDeclaration();
        }

        while (Current.Type == TokenType.KeywordImport)
        {
            imports.Add(ParseImportDeclaration());
        }

        while (Current.Type != TokenType.EndOfFile)
        {
            var previousPosition = _position;

            try
            {
                var declarationKind = Current.Type == TokenType.KeywordPub ? Peek(1).Type : Current.Type;

                if (declarationKind == TokenType.KeywordEnum)
                {
                    var parsedEnum = ParseEnumDeclaration();
                    (parsedEnum.TypeParameters.Count > 0 ? genericEnums : enums).Add(parsedEnum);
                }
                else if (declarationKind == TokenType.KeywordStruct)
                {
                    var parsedStruct = ParseStructDeclaration();
                    (parsedStruct.TypeParameters.Count > 0 ? genericStructs : structs).Add(parsedStruct);
                }
                else if (declarationKind == TokenType.KeywordExtern)
                {
                    externFunctions.Add(ParseExternFunction());
                }
                else
                {
                    var parsedFunction = ParseFunction();
                    (parsedFunction.TypeParameters.Count > 0 ? genericFunctions : functions).Add(parsedFunction);
                }
            }
            catch (ParseException)
            {
                SynchronizeTopLevel();
            }

            // Ensure we always make progress.
            // Prevents an infinite loop if recovery didn't advance.
            if (_position == previousPosition &&
                Current.Type != TokenType.EndOfFile)
            {
                Advance();
            }
        }

        var startSpan = module?.Span ?? (imports.Count > 0 ? imports[0].Span :
            functions.Count > 0 ? functions[0].Span :
            structs.Count > 0 ? structs[0].Span : Current.Span);
        var endSpan = structs.Count > 0 ? structs[^1].Span :
            functions.Count > 0 ? functions[^1].Span :
            imports.Count > 0 ? imports[^1].Span : module?.Span ?? Current.Span;

        var span = CreateSpan(startSpan.Start, endSpan.End);

        var program = new ProgramNode(module, imports, functions, structs, enums, externFunctions, span, fileName);
        program.GenericFunctions.AddRange(genericFunctions);
        program.GenericStructs.AddRange(genericStructs);
        program.GenericEnums.AddRange(genericEnums);
        return program;
    }

    private HashSet<string> CollectGenericNames()
    {
        // The prelude generics (Option, Result) are available in every file.
        // Generics declared by imported modules are usable by name too.
        var names = new HashSet<string>(BuiltinGenericRegistry.Names.Concat(importedGenericNames ?? []));

        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Type is TokenType.KeywordFunc or TokenType.KeywordStruct or TokenType.KeywordEnum &&
                tokens[i + 1].Type == TokenType.Identifier &&
                tokens[i + 2].Type == TokenType.LessThan)
            {
                names.Add(tokens[i + 1].Value);
            }
        }

        return names;
    }

    private ModuleDeclarationNode ParseModuleDeclaration()
    {
        var start = Consume(TokenType.KeywordModule);
        var path = new List<string>();

        path.Add(Consume(TokenType.Identifier).Value);

        while (Current.Type == TokenType.DoubleColon)
        {
            Consume(TokenType.DoubleColon);
            path.Add(Consume(TokenType.Identifier).Value);
        }

        var end = Peek(-1); // Last consumed identifier
        return new ModuleDeclarationNode(path, CreateSpan(start.Span.Start, end.Span.End));
    }

    private ImportDeclarationNode ParseImportDeclaration()
    {
        var start = Consume(TokenType.KeywordImport);
        var path = new List<string>();

        path.Add(Consume(TokenType.Identifier).Value);

        while (Current.Type == TokenType.DoubleColon)
        {
            Consume(TokenType.DoubleColon);
            path.Add(Consume(TokenType.Identifier).Value);
        }

        var end = Peek(-1); // Last consumed identifier
        return new ImportDeclarationNode(path, CreateSpan(start.Span.Start, end.Span.End));
    }

    private void SynchronizeTopLevel()
    {
        while (Current.Type != TokenType.EndOfFile &&
               Current.Type != TokenType.KeywordFunc &&
               Current.Type != TokenType.KeywordPub)
        {
            Advance();
        }
    }

    private SourceSpan CreateSpan(SourceLocation start, SourceLocation end)
    {
        return new SourceSpan(start, end);
    }

    private void Report(string code, string message, SourceSpan? span)
    {
        diagnostics.Report(
            new Diagnostic(
                fileName,
                code,
                DiagnosticSeverity.Error,
                message,
                span));
    }

    private sealed class ParseException : Exception;
}
