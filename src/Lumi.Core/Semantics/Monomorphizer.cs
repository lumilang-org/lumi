using System.Text;
using System.Text.RegularExpressions;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

// Expands generic templates (`struct Box<T>`, `func f<T>`) into concrete declarations before type
// checking. Every `Name<Args>` in the source becomes the mangled name of a specialization
// (`Box__i32`), which is a plain struct or function that the checker and code generator handle
// like any other. Templates themselves are never checked or emitted.
//
// Generics are resolved inside the module that declares them.
public sealed class Monomorphizer(DiagnosticBag diagnostics)
{
    private const int MaxInstantiationDepth = 24;

    private static readonly HashSet<string> BuiltinTypes =
    [
        TypeConst.I8, TypeConst.I16, TypeConst.I32, TypeConst.I64,
        TypeConst.U8, TypeConst.U16, TypeConst.U32, TypeConst.U64, TypeConst.Byte, TypeConst.Usize,
        TypeConst.F16, TypeConst.F32, TypeConst.F64,
        TypeConst.Bool, TypeConst.Char, TypeConst.String, TypeConst.Void
    ];

    private readonly Dictionary<ProgramNode, ModuleExpansion> _expansions = new();

    // Specializes a generic template of `module` on demand (used by the checker for built-ins such
    // as `slice.get`). Returns the specialization's name in the module, or null when unavailable.
    public string? Specialize(
        ProgramNode module, string template, IReadOnlyList<string> arguments, ASTNode owner, out bool created)
    {
        created = false;

        if (!_expansions.TryGetValue(module, out var expansion))
        {
            return null;
        }

        var before = module.Enums.Count + module.Structs.Count + module.Functions.Count;
        var name = expansion.SpecializeOnDemand(template, arguments.ToList(), owner);
        created = module.Enums.Count + module.Structs.Count + module.Functions.Count > before;
        return name;
    }

    public void Expand(IReadOnlyList<ProgramNode> modules)
    {
        var knownTypes = new HashSet<string>(BuiltinTypes);
        foreach (var module in modules)
        {
            foreach (var name in module.Structs.Select(s => s.Name)
                         .Concat(module.GenericStructs.Select(s => s.Name))
                         .Concat(module.Enums.Select(e => e.Name)))
            {
                knownTypes.Add(name);
            }
        }

        foreach (var module in modules)
        {
            AddPrelude(module);

            if (module.GenericFunctions.Count > 0 || module.GenericStructs.Count > 0 ||
                module.GenericEnums.Count > 0)
            {
                var expansion = new ModuleExpansion(diagnostics, module, knownTypes);
                _expansions[module] = expansion;
                expansion.Run();
            }
        }
    }

    // Makes the built-in generics (Option, Result) usable in `module` unless it declares the name itself.
    private static void AddPrelude(ProgramNode module)
    {
        var declared = module.Structs.Select(s => s.Name)
            .Concat(module.Enums.Select(e => e.Name))
            .Concat(module.GenericStructs.Select(s => s.Name))
            .Concat(module.GenericEnums.Select(e => e.Name))
            .Concat(module.GenericFunctions.Select(f => f.Name));

        var source = BuiltinGenericRegistry.PreludeSource(declared);
        if (source.Length == 0)
        {
            return;
        }

        var preludeDiagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, preludeDiagnostics, "<prelude>").Tokenize();
        var prelude = new Parser(tokens, preludeDiagnostics, "<prelude>").ParseProgram();
        module.GenericEnums.AddRange(prelude.GenericEnums);
    }

    private sealed class ModuleExpansion
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly ProgramNode _module;
        private readonly HashSet<string> _knownTypes;
        private readonly Dictionary<string, FunctionDeclarationNode> _functionTemplates = new();
        private readonly Dictionary<string, StructDeclarationNode> _structTemplates = new();
        private readonly Dictionary<string, EnumDeclarationNode> _enumTemplates = new();
        private readonly Dictionary<string, string?> _cache = new();
        private readonly Dictionary<ASTNode, int> _depths = new();
        private readonly Queue<ASTNode> _queue = new();
        private int _currentDepth;

        public ModuleExpansion(DiagnosticBag diagnostics, ProgramNode module, HashSet<string> knownTypes)
        {
            _diagnostics = diagnostics;
            _module = module;
            _knownTypes = knownTypes;
        }

        public void Run()
        {
            foreach (var function in _module.GenericFunctions)
            {
                if (!_functionTemplates.TryAdd(function.Name, function))
                {
                    Report(DiagnosticCodes.DuplicateFunction,
                        $"Generic function '{function.Name}' is already declared.", function);
                }
            }

            foreach (var structDecl in _module.GenericStructs)
            {
                if (!_structTemplates.TryAdd(structDecl.Name, structDecl))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Generic struct '{structDecl.Name}' is already declared.", structDecl);
                }
            }

            foreach (var enumDecl in _module.GenericEnums)
            {
                if (!_enumTemplates.TryAdd(enumDecl.Name, enumDecl))
                {
                    Report(DiagnosticCodes.TypeMismatch,
                        $"Generic enum '{enumDecl.Name}' is already declared.", enumDecl);
                }
            }

            foreach (var template in _module.GenericFunctions.Concat<ASTNode>(_module.GenericStructs)
                         .Concat(_module.GenericEnums))
            {
                ValidateTemplate(template);
            }

            var originals = new List<ASTNode>();
            originals.AddRange(_module.Functions);
            originals.AddRange(_module.Structs);
            originals.AddRange(_module.Enums);
            originals.AddRange(_module.ExternFunctions);

            foreach (var declaration in originals)
            {
                Process(declaration, 0);
            }

            while (_queue.Count > 0)
            {
                var declaration = _queue.Dequeue();
                Process(declaration, _depths[declaration]);
            }
        }

        public string? SpecializeOnDemand(string template, List<string> arguments, ASTNode owner)
        {
            _currentDepth = 0;
            var name = Instantiate(template, arguments, owner);

            while (_queue.Count > 0)
            {
                var declaration = _queue.Dequeue();
                Process(declaration, _depths[declaration]);
            }

            return name;
        }

        private void Process(ASTNode declaration, int depth)
        {
            _currentDepth = depth;
            AstRewriter.Rewrite(declaration, (owner, text) => text.Contains('<') ? Resolve(text, owner) : text);
        }

        private void ValidateTemplate(ASTNode template)
        {
            var constraints = template switch
            {
                FunctionDeclarationNode f => f.Constraints,
                StructDeclarationNode s => s.Constraints,
                EnumDeclarationNode e => e.Constraints,
                _ => []
            };

            foreach (var constraint in constraints)
            {
                var bound = Regex.Match(constraint.BoundType, @"[A-Za-z_]\w*(::\w+)*").Value;
                var typeParameters = template switch
                {
                    FunctionDeclarationNode fn => fn.TypeParameters,
                    StructDeclarationNode st => st.TypeParameters,
                    _ => ((EnumDeclarationNode)template).TypeParameters
                };

                if (bound.Length > 0 && !bound.Contains("::") && !_knownTypes.Contains(bound) &&
                    !typeParameters.Contains(bound))
                {
                    Report(DiagnosticCodes.InvalidTypeParameters,
                        $"Constraint bound '{constraint.BoundType}' on '{constraint.Parameter}' is not a known type.",
                        constraint);
                }
            }
        }

        // Replaces every `Name<A, B>` naming a template in `text` with its specialization's name.
        private string Resolve(string text, ASTNode owner)
        {
            var result = new StringBuilder();
            var i = 0;

            while (i < text.Length)
            {
                if (!IsIdentifierStart(text[i]) || (i > 0 && (IsIdentifierPart(text[i - 1]) || text[i - 1] is ':' or '.')))
                {
                    result.Append(text[i]);
                    i++;
                    continue;
                }

                var start = i;
                while (i < text.Length && IsIdentifierPart(text[i]))
                {
                    i++;
                }

                var word = text[start..i];

                if (i < text.Length && text[i] == '<' &&
                    (_structTemplates.ContainsKey(word) || _functionTemplates.ContainsKey(word) ||
                     _enumTemplates.ContainsKey(word)))
                {
                    var close = FindClosingAngle(text, i);

                    if (close > 0)
                    {
                        var arguments = SplitArguments(text[(i + 1)..close])
                            .Select(argument => Resolve(argument.Trim(), owner))
                            .ToList();

                        result.Append(Instantiate(word, arguments, owner) ?? TypeConst.Error);
                        i = close + 1;
                        continue;
                    }
                }

                result.Append(word);
            }

            return result.ToString();
        }

        private string? Instantiate(string name, List<string> arguments, ASTNode owner)
        {
            var isStruct = _structTemplates.TryGetValue(name, out var structTemplate);
            var isEnum = !isStruct && _enumTemplates.TryGetValue(name, out var enumTemplate);
            _functionTemplates.TryGetValue(name, out var functionTemplate);
            enumTemplate = isEnum ? _enumTemplates[name] : null;

            var typeParameters = isStruct ? structTemplate!.TypeParameters
                : isEnum ? enumTemplate!.TypeParameters : functionTemplate!.TypeParameters;
            var constraints = isStruct ? structTemplate!.Constraints
                : isEnum ? enumTemplate!.Constraints : functionTemplate!.Constraints;

            if (arguments.Count != typeParameters.Count)
            {
                Report(DiagnosticCodes.GenericArgumentMismatch,
                    $"'{name}' expects {typeParameters.Count} type argument(s) but {arguments.Count} were given.",
                    owner);
                return null;
            }

            var key = $"{name}<{string.Join(", ", arguments)}>";

            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var substitutions = new Dictionary<string, string>();
            for (var i = 0; i < typeParameters.Count; i++)
            {
                substitutions[typeParameters[i]] = arguments[i];
            }

            foreach (var constraint in constraints)
            {
                var bound = Resolve(Substitute(constraint.BoundType, substitutions), owner);
                var argument = substitutions[constraint.Parameter];

                if (argument != bound)
                {
                    Report(DiagnosticCodes.ConstraintNotSatisfied,
                        $"Type '{argument}' does not satisfy the constraint '{constraint.Parameter} : {bound}' " +
                        $"required by '{name}'.",
                        owner);
                    _cache[key] = null;
                    return null;
                }
            }

            if (_currentDepth + 1 > MaxInstantiationDepth)
            {
                Report(DiagnosticCodes.GenericInstantiationTooDeep,
                    $"Instantiating '{key}' nests generic types more than {MaxInstantiationDepth} levels deep; " +
                    "the generic definition probably instantiates itself with ever larger types.",
                    owner);
                _cache[key] = null;
                return null;
            }

            var mangled = Mangle(name, arguments);
            _cache[key] = mangled;

            ASTNode specialization;

            if (isEnum)
            {
                var clone = AstRewriter.Clone(enumTemplate!, (_, text) => Substitute(text, substitutions));
                AstRewriter.SetProperty(clone, "Name", mangled);
                AstRewriter.SetProperty(clone, "GenericOrigin", name);
                AstRewriter.SetProperty(clone, "TypeParameters", new List<string>());
                AstRewriter.SetProperty(clone, "Constraints", new List<TypeConstraintNode>());
                _module.AddSpecialization(clone);
                specialization = clone;
            }
            else if (isStruct)
            {
                var clone = AstRewriter.Clone(structTemplate!, (_, text) => Substitute(text, substitutions));
                AstRewriter.SetProperty(clone, "Name", mangled);
                AstRewriter.SetProperty(clone, "TypeParameters", new List<string>());
                AstRewriter.SetProperty(clone, "Constraints", new List<TypeConstraintNode>());
                _module.AddSpecialization(clone);
                specialization = clone;
            }
            else
            {
                var clone = AstRewriter.Clone(functionTemplate!, (_, text) => Substitute(text, substitutions));
                AstRewriter.SetProperty(clone, "Name", mangled);
                AstRewriter.SetProperty(clone, "TypeParameters", new List<string>());
                AstRewriter.SetProperty(clone, "Constraints", new List<TypeConstraintNode>());
                _module.AddSpecialization(clone);
                specialization = clone;
            }

            _depths[specialization] = _currentDepth + 1;
            _queue.Enqueue(specialization);
            return mangled;
        }

        private static string Substitute(string text, Dictionary<string, string> substitutions)
        {
            return Regex.Replace(
                text,
                @"(?<![\w:.])[A-Za-z_]\w*(?![\w])",
                match => substitutions.TryGetValue(match.Value, out var replacement) ? replacement : match.Value);
        }

        public static string Mangle(string name, IReadOnlyList<string> arguments)
        {
            var parts = arguments.Select(argument => argument
                .Replace("::", "_")
                .Replace("*", "ptr_")
                .Replace("[", "_arr")
                .Replace("]", "")
                .Replace("(", "tup_")
                .Replace(")", "")
                .Replace(", ", "_")
                .Replace(",", "_")
                .Replace(" ", ""));

            return $"{name}__{string.Join("_", parts)}";
        }

        private static int FindClosingAngle(string text, int open)
        {
            var depth = 0;

            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    depth++;
                }
                else if (text[i] == '>')
                {
                    depth--;

                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static List<string> SplitArguments(string text)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;

            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '<' or '(' or '[':
                        depth++;
                        break;
                    case '>' or ')' or ']':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        parts.Add(text[start..i]);
                        start = i + 1;
                        break;
                }
            }

            parts.Add(text[start..]);
            return parts;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private void Report(string code, string message, ASTNode node)
        {
            _diagnostics.Report(_module.FileName, code, message, node.Span);
        }
    }
}
