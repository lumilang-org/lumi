using System.Globalization;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;

namespace Lumi.Core.Syntax;

public sealed class Parser(
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

    private Precedence GetPrecedence(TokenType type)
    {
        return type switch
        {
            TokenType.LBracket => Precedence.Postfix,
            TokenType.LogicalOr => Precedence.LogicalOr,

            TokenType.LogicalAnd => Precedence.LogicalAnd,

            TokenType.BitwiseOr => Precedence.BitwiseOr,

            TokenType.BitwiseXor => Precedence.BitwiseXor,

            TokenType.BitwiseAnd => Precedence.BitwiseAnd,

            TokenType.LeftShift => Precedence.Shift,

            TokenType.RightShift => Precedence.Shift,

            TokenType.EqualEqual
                or TokenType.BangEqual
                or TokenType.LessThan
                or TokenType.LessThanOrEqual
                or TokenType.GreaterThan
                or TokenType.GreaterThanOrEqual
                => Precedence.Comparison,

            TokenType.DotDot => Precedence.Range,

            TokenType.Plus or TokenType.Minus
                => Precedence.Sum,

            TokenType.Star or TokenType.Slash
                => Precedence.Product,

            _ => Precedence.Lowest
        };
    }

    // `-> Type`; a function without an arrow returns void.
    private string ParseOptionalReturnType()
    {
        if (Current.Type != TokenType.Arrow)
        {
            return TypeConst.Void;
        }

        Consume(TokenType.Arrow);

        if (Current.Type != TokenType.TypeVoid &&
            !IsTypeToken(Current.Type) &&
            Current.Type != TokenType.Identifier &&
            Current.Type != TokenType.LParen &&
            Current.Type != TokenType.Star)
        {
            Report(
                DiagnosticCodes.ExpectedToken,
                $"Syntax Error: Expected return type, got {Current.Type} ('{Current.Value}') at position {_position}",
                Current.Span);
            throw new ParseException();
        }

        var returnType = Current.Type == TokenType.TypeVoid ? TypeConst.Void : ParseTypeName();
        if (returnType == TypeConst.Void)
        {
            Advance();
        }

        return returnType;
    }

    private ExternFunctionDeclarationNode ParseExternFunction()
    {
        var visibility = VisibilityModifier.Private;
        var externStart = Current;
        if (Current.Type == TokenType.KeywordPub)
        {
            visibility = VisibilityModifier.Public;
            Consume(TokenType.KeywordPub);
        }

        Consume(TokenType.KeywordExtern);
        Consume(TokenType.KeywordFunc);

        var funcName = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.LParen);
        var parameters = ParseFunctionParameters();
        Consume(TokenType.RParen);

        var returnType = ParseOptionalReturnType();

        if (Current.Type == TokenType.LBrace)
        {
            Report(
                DiagnosticCodes.UnexpectedToken,
                $"Syntax Error: Extern function '{funcName}' cannot have a body",
                Current.Span);
            throw new ParseException();
        }

        // The declaration ends with its return type, not with the token that follows it.
        var span = CreateSpan(externStart.Span.Start, Peek(-1).Span.End);

        return new ExternFunctionDeclarationNode(
            funcName,
            parameters,
            returnType,
            span,
            visibility);
    }

    private FunctionDeclarationNode ParseFunction(bool isMethod = false)
    {
        var currentModifier = VisibilityModifier.Private;

        if (Current.Type == TokenType.KeywordPub)
        {
            currentModifier = VisibilityModifier.Public;
            Consume(TokenType.KeywordPub);
        }

        var start = Consume(TokenType.KeywordFunc);

        var funcName = Current.Value;
        var funcNameToken = Consume(TokenType.Identifier);

        var typeParameters = ParseTypeParameters();

        if (isMethod && typeParameters.Count > 0)
        {
            Report(
                DiagnosticCodes.InvalidTypeParameters,
                $"Struct function '{funcName}' cannot declare its own type parameters; " +
                "use the type parameters of the struct.",
                funcNameToken.Span);
        }

        Consume(TokenType.LParen);

        var parameters = ParseFunctionParameters();

        Consume(TokenType.RParen);
        var returnType = ParseOptionalReturnType();
        var constraints = ParseWhereClause(typeParameters);

        Consume(TokenType.LBrace);

        var body = new List<StatementNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            body.Add(ParseStatement());
        }

        var closingBrace = Consume(TokenType.RBrace);

        var span = CreateSpan(
            start.Span.Start,
            closingBrace.Span.End);

        return new FunctionDeclarationNode(
            funcName,
            parameters,
            returnType,
            body,
            span,
            currentModifier,
            typeParameters,
            constraints);
    }

    // `<T, U>` after a declaration name; empty when absent.
    private List<string> ParseTypeParameters()
    {
        var names = new List<string>();

        if (Current.Type != TokenType.LessThan)
        {
            return names;
        }

        var open = Consume(TokenType.LessThan);

        if (Current.Type == TokenType.GreaterThan)
        {
            Report(
                DiagnosticCodes.InvalidTypeParameters,
                "Syntax Error: A type parameter list cannot be empty; write '<T>' or remove '<>'.",
                open.Span);
            throw new ParseException();
        }

        while (true)
        {
            var nameToken = Consume(TokenType.Identifier);

            if (names.Contains(nameToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Duplicate type parameter '{nameToken.Value}'.",
                    nameToken.Span);
            }
            else
            {
                names.Add(nameToken.Value);
            }

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        Consume(TokenType.GreaterThan);
        return names;
    }

    // `where T : Vec2, U : i32`
    private List<TypeConstraintNode> ParseWhereClause(IReadOnlyList<string> typeParameters)
    {
        var constraints = new List<TypeConstraintNode>();

        if (Current.Type != TokenType.KeywordWhere)
        {
            return constraints;
        }

        Consume(TokenType.KeywordWhere);

        while (true)
        {
            var parameterToken = Consume(TokenType.Identifier);
            Consume(TokenType.Colon);
            var boundStart = Current;
            var bound = ParseTypeName();
            var span = CreateSpan(parameterToken.Span.Start, Peek(-1).Span.End);

            if (!typeParameters.Contains(parameterToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Constraint refers to '{parameterToken.Value}', which is not a type parameter of this declaration.",
                    parameterToken.Span);
            }
            else if (constraints.Any(c => c.Parameter == parameterToken.Value))
            {
                Report(
                    DiagnosticCodes.InvalidTypeParameters,
                    $"Type parameter '{parameterToken.Value}' already has a bound.",
                    boundStart.Span);
            }
            else
            {
                constraints.Add(new TypeConstraintNode(parameterToken.Value, bound, span));
            }

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        return constraints;
    }

    // `<A, B>` after a generic name in a type or expression, as the canonical text "<A, B>".
    private string ParseTypeArguments()
    {
        var open = Consume(TokenType.LessThan);

        if (Current.Type == TokenType.GreaterThan)
        {
            Report(
                DiagnosticCodes.InvalidTypeParameters,
                "Syntax Error: A type argument list cannot be empty.",
                open.Span);
            throw new ParseException();
        }

        var arguments = new List<string> { ParseTypeName() };

        while (Current.Type == TokenType.Comma)
        {
            Consume(TokenType.Comma);
            arguments.Add(ParseTypeName());
        }

        ExpectCloseAngle();
        return "<" + string.Join(", ", arguments) + ">";
    }

    // Closes a type-argument list; `>>` closes two nested lists (`Box<Box<i32>>`).
    private void ExpectCloseAngle()
    {
        if (_pendingCloseAngle > 0)
        {
            _pendingCloseAngle--;
            return;
        }

        if (Current.Type == TokenType.GreaterThan)
        {
            Advance();
            return;
        }

        if (Current.Type == TokenType.RightShift)
        {
            Advance();
            _pendingCloseAngle = 1;
            return;
        }

        Report(
            DiagnosticCodes.ExpectedToken,
            $"Syntax Error: Expected '>' to close the type argument list, got {Current.Type} ('{Current.Value}')",
            Current.Span);
        throw new ParseException();
    }

    // Returns the offset just past a well-formed `<...>` starting at `offset`, or -1.
    private int ScanTypeArguments(int offset)
    {
        if (Peek(offset).Type != TokenType.LessThan)
        {
            return -1;
        }

        var depth = 0;

        while (true)
        {
            var type = Peek(offset).Type;

            switch (type)
            {
                case TokenType.LessThan:
                    depth++;
                    break;
                case TokenType.GreaterThan:
                    depth--;
                    break;
                case TokenType.RightShift:
                    depth -= 2;
                    break;
                case TokenType.Identifier or TokenType.Star or TokenType.Comma or TokenType.LParen
                    or TokenType.RParen or TokenType.LBracket or TokenType.RBracket or TokenType.Number
                    or TokenType.DoubleColon:
                    break;
                default:
                    if (!IsTypeToken(type))
                    {
                        return -1;
                    }

                    break;
            }

            offset++;

            if (depth < 0)
            {
                return -1;
            }

            if (depth == 0)
            {
                return offset;
            }
        }
    }

    private bool IsGenericName(string name)
    {
        var separator = name.LastIndexOf("::", StringComparison.Ordinal);
        return _genericNames.Contains(separator >= 0 ? name[(separator + 2)..] : name);
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

    private StructDeclarationNode ParseStructDeclaration()
    {
        var currentModifier = VisibilityModifier.Private;
        if (Current.Type == TokenType.KeywordPub)
        {
            currentModifier = VisibilityModifier.Public;
            Consume(TokenType.KeywordPub);
        }

        var start = Consume(TokenType.KeywordStruct);

        var nameToken = Current;
        var structName = nameToken.Value;
        Consume(TokenType.Identifier);

        var typeParameters = ParseTypeParameters();
        var constraints = ParseWhereClause(typeParameters);

        Consume(TokenType.LBrace);

        var fields = new List<StructFieldNode>();
        var methods = new List<FunctionDeclarationNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            if (Current.Type == TokenType.KeywordFunc ||
                (Current.Type == TokenType.KeywordPub && Peek(1).Type == TokenType.KeywordFunc))
            {
                methods.Add(ParseFunction(isMethod: true));
                continue;
            }

            var fieldModifier = VisibilityModifier.Private;
            if (Current.Type == TokenType.KeywordPub)
            {
                fieldModifier = VisibilityModifier.Public;
                Consume(TokenType.KeywordPub);
            }

            if (!IsTypeToken(Current.Type) &&
                Current.Type != TokenType.Identifier &&
                Current.Type != TokenType.LParen &&
                Current.Type != TokenType.Star)
            {
                Report(
                    DiagnosticCodes.ExpectedToken,
                    $"Syntax Error: Expected field type, got {Current.Type} ('{Current.Value}') at position {_position}",
                    Current.Span);
                throw new ParseException();
            }

            var fieldTypeToken = Current;
            var fieldType = ParseTypeName();

            var fieldNameToken = Current;
            var fieldName = fieldNameToken.Value;
            Consume(TokenType.Identifier);

            var fieldSpan = CreateSpan(
                fieldTypeToken.Span.Start,
                fieldNameToken.Span.End);

            fields.Add(new StructFieldNode(fieldType, fieldName, fieldSpan, fieldModifier));

            if (Current.Type == TokenType.Colon)
            {
                Consume(TokenType.Colon);
            }
        }

        var closingBrace = Consume(TokenType.RBrace);

        var span = CreateSpan(
            start.Span.Start,
            closingBrace.Span.End);

        return new StructDeclarationNode(
            structName,
            fields,
            methods,
            span,
            currentModifier,
            typeParameters,
            constraints);
    }

    private EnumDeclarationNode ParseEnumDeclaration()
    {
        var visibility = VisibilityModifier.Private;
        var startToken = Current;
        if (Current.Type == TokenType.KeywordPub)
        {
            visibility = VisibilityModifier.Public;
            Consume(TokenType.KeywordPub);
        }

        Consume(TokenType.KeywordEnum);
        var nameToken = Consume(TokenType.Identifier);
        var typeParameters = ParseTypeParameters();
        var constraints = ParseWhereClause(typeParameters);
        Consume(TokenType.LBrace);

        var variants = new List<EnumVariantNode>();

        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EndOfFile)
        {
            var variantToken = Consume(TokenType.Identifier);
            var fields = new List<EnumVariantFieldNode>();
            var kind = EnumVariantKind.Unit;

            if (Current.Type == TokenType.LParen)
            {
                kind = EnumVariantKind.Tuple;
                Consume(TokenType.LParen);

                while (Current.Type != TokenType.RParen && Current.Type != TokenType.EndOfFile)
                {
                    var typeToken = Current;
                    var type = ParseTypeName();
                    fields.Add(new EnumVariantFieldNode(null, type,
                        CreateSpan(typeToken.Span.Start, Peek(-1).Span.End)));

                    if (Current.Type != TokenType.Comma)
                    {
                        break;
                    }

                    Consume(TokenType.Comma);
                }

                Consume(TokenType.RParen);
            }
            else if (Current.Type == TokenType.LBrace)
            {
                kind = EnumVariantKind.Struct;
                Consume(TokenType.LBrace);

                while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EndOfFile)
                {
                    var fieldToken = Consume(TokenType.Identifier);
                    Consume(TokenType.Colon);
                    var type = ParseTypeName();
                    fields.Add(new EnumVariantFieldNode(fieldToken.Value, type,
                        CreateSpan(fieldToken.Span.Start, Peek(-1).Span.End)));

                    if (Current.Type != TokenType.Comma)
                    {
                        break;
                    }

                    Consume(TokenType.Comma);
                }

                Consume(TokenType.RBrace);
            }

            variants.Add(new EnumVariantNode(
                variantToken.Value,
                kind,
                fields,
                CreateSpan(variantToken.Span.Start, Peek(-1).Span.End)));

            if (Current.Type == TokenType.Comma)
            {
                Consume(TokenType.Comma);
            }
        }

        var end = Consume(TokenType.RBrace);

        return new EnumDeclarationNode(
            nameToken.Value,
            variants,
            CreateSpan(startToken.Span.Start, end.Span.End),
            visibility,
            typeParameters,
            constraints);
    }

    // True while parsing a switch pattern, where `Enum.Variant { x, y }` may bind by field name.
    private bool _inSwitchPattern;

    private bool IsEnumStructBodyStart()
    {
        if (Current.Type != TokenType.LBrace || Peek(1).Type != TokenType.Identifier)
        {
            return false;
        }

        var after = Peek(2).Type;
        return after == TokenType.Colon ||
               (_inSwitchPattern && after is TokenType.Comma or TokenType.RBrace);
    }

    private StructLiteralNode ParseStructLiteral(Token identifierToken)
    {
        var structName = identifierToken.Value;
        var initializers = ParseFieldInitializers(out var closingBrace);

        var span = CreateSpan(
            identifierToken.Span.Start,
            closingBrace.Span.End);

        return new StructLiteralNode(
            structName,
            initializers,
            span);
    }

    // `{ name: value, ... }`
    private List<StructFieldInitNode> ParseFieldInitializers(out Token closingBrace)
    {
        Consume(TokenType.LBrace);

        var initializers = new List<StructFieldInitNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            var fieldToken = Current;
            var fieldName = fieldToken.Value;
            Consume(TokenType.Identifier);

            ExpressionNode valueExpr;
            if (_inSwitchPattern && Current.Type == TokenType.Colon)
            {
                Report(
                    DiagnosticCodes.UnexpectedToken,
                    $"Syntax Error: Struct patterns bind fields by name; write '{fieldName}' instead of '{fieldName}: ...'.",
                    Current.Span);
                throw new ParseException();
            }

            if (_inSwitchPattern)
            {
                // Shorthand: `x` binds the field `x` to a local named `x`.
                valueExpr = new IdentifierNode(fieldName, fieldToken.Span);
            }
            else
            {
                Consume(TokenType.Colon);
                valueExpr = ParseExpression();
            }

            var initSpan = CreateSpan(
                fieldToken.Span.Start,
                valueExpr.Span!.End);

            initializers.Add(new StructFieldInitNode(fieldName, valueExpr, initSpan));

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        closingBrace = Consume(TokenType.RBrace);
        return initializers;
    }

    private string ParseTypeName()
    {
        var pointerDepth = 0;
        while (Current.Type == TokenType.Star)
        {
            Advance();
            pointerDepth++;
        }

        string typeName;

        if (Current.Type == TokenType.LParen)
        {
            // *(i32[3]) is a pointer to an array type; (A, B) is a tuple type.
            var open = Consume(TokenType.LParen);
            var elements = new List<string> { ParseTypeName() };

            while (Current.Type == TokenType.Comma)
            {
                Consume(TokenType.Comma);
                elements.Add(ParseTypeName());
            }

            Consume(TokenType.RParen);

            if (elements.Count > 1)
            {
                typeName = TupleTypes.Make(elements);
            }
            else if (pointerDepth > 0)
            {
                typeName = elements[0];
            }
            else
            {
                Report(
                    DiagnosticCodes.ExpectedToken,
                    "Syntax Error: A tuple type needs at least two elements.",
                    open.Span);
                throw new ParseException();
            }
        }
        else
        {
            if (!IsTypeToken(Current.Type) && Current.Type != TokenType.Identifier)
            {
                Report(
                    DiagnosticCodes.ExpectedToken,
                    $"Syntax Error: Expected type name, got {Current.Type} ('{Current.Value}') at position {_position}",
                    Current.Span);
                throw new ParseException();
            }

            typeName = Current.Value;
            Advance();

            while (Current.Type == TokenType.DoubleColon)
            {
                Consume(TokenType.DoubleColon);
                typeName += "::" + Consume(TokenType.Identifier).Value;
            }

            if (Current.Type == TokenType.LessThan && IsGenericName(typeName))
            {
                typeName += ParseTypeArguments();
            }
        }

        for (var i = 0; i < pointerDepth; i++)
        {
            typeName = PointerTypes.To(typeName);
        }

        while (Current.Type == TokenType.LBracket)
        {
            Consume(TokenType.LBracket);

            // `T[]` is a slice, `T[N]` a fixed-size array.
            if (Current.Type == TokenType.RBracket)
            {
                Consume(TokenType.RBracket);
                typeName += "[]";
                continue;
            }

            var lengthToken = Consume(TokenType.Number);
            Consume(TokenType.RBracket);
            typeName += $"[{lengthToken.Value}]";
        }

        return typeName;
    }


    private IReadOnlyList<FunctionParameterNode> ParseFunctionParameters()
    {
        var parameters = new List<FunctionParameterNode>();

        if (Current.Type == TokenType.RParen)
        {
            return parameters;
        }

        while (true)
        {
            var typeStart = Current.Span.Start;
            var type = ParseTypeName();

            var nameToken = Current;
            var name = nameToken.Value;
            Consume(TokenType.Identifier);

            var span = CreateSpan(
                typeStart,
                nameToken.Span.End);

            parameters.Add(new FunctionParameterNode(type, name, span));

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        return parameters;
    }

    private bool IsVariableDeclaration()
    {
        var offset = 0;
        while (Peek(offset).Type == TokenType.Star)
        {
            offset++;
        }

        if (offset == 0 && (IsTypeToken(Current.Type) || Current.Type == TokenType.KeywordLet))
        {
            return true;
        }

        // `(A, B) name`: a tuple type, told apart from a grouped expression by its comma.
        if (offset == 0 && Current.Type == TokenType.LParen)
        {
            var depth = 0;
            var hasComma = false;
            var i = 0;
            do
            {
                switch (Peek(i).Type)
                {
                    case TokenType.LParen:
                        depth++;
                        break;
                    case TokenType.RParen:
                        depth--;
                        break;
                    case TokenType.Comma when depth == 1:
                        hasComma = true;
                        break;
                    case TokenType.EndOfFile:
                        return false;
                }

                i++;
            } while (depth > 0);

            return hasComma && IsTypeSuffixThenName(i);
        }

        // `*(T) name`: skip the parenthesised pointee type.
        if (offset > 0 && Peek(offset).Type == TokenType.LParen)
        {
            var depth = 0;
            do
            {
                switch (Peek(offset).Type)
                {
                    case TokenType.LParen:
                        depth++;
                        break;
                    case TokenType.RParen:
                        depth--;
                        break;
                    case TokenType.EndOfFile:
                        return false;
                }

                offset++;
            } while (depth > 0);

            return IsTypeSuffixThenName(offset);
        }

        var first = Peek(offset).Type;
        if (first != TokenType.Identifier && !(offset > 0 && IsTypeToken(first)))
        {
            return false;
        }

        offset++;

        // A type name, possibly module-qualified (std::Vec), followed by a variable name.
        while (Peek(offset).Type == TokenType.DoubleColon)
        {
            offset++; // Consume '::'
            if (Peek(offset).Type != TokenType.Identifier)
            {
                return false;
            }

            offset++; // Consume identifier
        }

        // `Box<i32> name`: type arguments of a generic type.
        if (Peek(offset).Type == TokenType.LessThan && IsGenericName(Peek(offset - 1).Value))
        {
            offset = ScanTypeArguments(offset);
            if (offset < 0)
            {
                return false;
            }
        }

        return IsTypeSuffixThenName(offset);
    }

    // Optional `[]` / `[N]` suffixes followed by the declared variable's name.
    private bool IsTypeSuffixThenName(int offset)
    {
        while (Peek(offset).Type == TokenType.LBracket)
        {
            offset++; // Consume '['

            if (Peek(offset).Type == TokenType.RBracket)
            {
                offset++; // Consume ']' of `[]`
                continue;
            }

            if (Peek(offset).Type != TokenType.Number)
            {
                return false;
            }

            offset++; // Consume number
            if (Peek(offset).Type != TokenType.RBracket)
            {
                return false;
            }

            offset++; // Consume ']'
        }

        return Peek(offset).Type == TokenType.Identifier;
    }

    // A `*` that starts a new line and whose line assigns (`*p = 1`) begins a statement
    // instead of continuing the previous expression as a multiplication.
    private bool StartsDereferenceAssignment()
    {
        var line = Current.Span.Start.Line;
        if (line <= Peek(-1).Span.End.Line)
        {
            return false;
        }

        for (var offset = 0; Peek(offset).Span.Start.Line == line; offset++)
        {
            if (Peek(offset).Type == TokenType.Assign)
            {
                return true;
            }

            if (Peek(offset).Type == TokenType.EndOfFile)
            {
                break;
            }
        }

        return false;
    }

    private TupleDestructuringNode ParseTupleDestructuring()
    {
        var start = Consume(TokenType.KeywordLet);
        var pattern = ParseTuplePattern();
        RejectLetTypeAnnotation();
        Consume(TokenType.Assign);
        var initializer = ParseExpression();

        return new TupleDestructuringNode(
            pattern,
            initializer,
            CreateSpan(start.Span.Start, initializer.Span!.End));
    }

    private TuplePatternNode ParseTuplePattern()
    {
        var open = Consume(TokenType.LParen);
        var elements = new List<TuplePatternNode>();

        while (Current.Type != TokenType.RParen && Current.Type != TokenType.EndOfFile)
        {
            if (Current.Type == TokenType.LParen)
            {
                elements.Add(ParseTuplePattern());
            }
            else
            {
                var name = Consume(TokenType.Identifier);
                elements.Add(new TuplePatternNode(name.Value, null, name.Span));
            }

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        var close = Consume(TokenType.RParen);
        return new TuplePatternNode(null, elements, CreateSpan(open.Span.Start, close.Span.End));
    }

    private StatementNode ParseStatement()
    {
        if (Current.Type == TokenType.KeywordLet && Peek(1).Type == TokenType.LParen)
        {
            return ParseTupleDestructuring();
        }

        if (IsVariableDeclaration())
        {
            return ParseVariableDeclaration();
        }

        switch (Current.Type)
        {
            case TokenType.Identifier when Next.Type == TokenType.Assign:
            {
                return ParseAssignmentStatement();
            }
            case TokenType.Identifier when Next.Type == TokenType.LBracket || Next.Type == TokenType.Dot:
            {
                return ParseTargetAssignmentStatement();
            }
            case TokenType.Identifier when Next.Type == TokenType.LParen:
            {
                return ParseExpressionStatement();
            }
            case TokenType.KeywordReturn:
            {
                var start = Current.Span.Start;
                var returnToken = Consume(TokenType.KeywordReturn);

                ExpressionNode? value = null;

                if (Current.Type != TokenType.RBrace &&
                    Current.Type != TokenType.EndOfFile)
                {
                    value = ParseExpression();
                }

                var end = value?.Span?.End ?? returnToken.Span.End;
                var span = CreateSpan(start, end);

                return new ReturnStatementNode(value, span);
            }
            case TokenType.KeywordDefer:
                return ParseDeferStatement();
            case TokenType.KeywordIf:
                return ParseIfStatement();
            case TokenType.KeywordWhile:
                return ParseWhileStatement();
            case TokenType.KeywordSwitch:
                return ParseSwitchStatement();
            case TokenType.KeywordBreak:
            {
                var token = Consume(TokenType.KeywordBreak);
                return new BreakStatementNode(token.Span);
            }

            case TokenType.KeywordContinue:
            {
                var token = Consume(TokenType.KeywordContinue);
                return new ContinueStatementNode(token.Span);
            }
            case TokenType.PrintFunc:
            {
                var start = Current.Span.Start;
                var functionName = Current.Value;
                var appendNewline = functionName == "println";

                Consume(TokenType.PrintFunc);
                Consume(TokenType.LParen);

                if (Current.Type == TokenType.RParen)
                {
                    Report(
                        DiagnosticCodes.EmptyPrintExpression,
                        $"{Current.Value}: expected an expression inside {functionName}.",
                        Current.Span);
                    throw new ParseException();
                }

                var expression = ParseExpression();
                var closingParen = Consume(TokenType.RParen);

                var span = CreateSpan(start, closingParen.Span.End);

                return new PrintStatementNode(expression, span, appendNewline);
            }
            case TokenType.Identifier when Next.Type == TokenType.Assign:
            {
                return ParseAssignmentStatement();
            }
            case TokenType.KeywordFor:
            {
                return ParseForStatement();
            }

            default:
            {
                var expr = ParseExpression();

                if (Current.Type == TokenType.Assign)
                {
                    if (expr is not IdentifierNode and not MemberAccessNode
                        and not IndexExpressionNode and not DereferenceExpressionNode)
                    {
                        Report(
                            DiagnosticCodes.UnexpectedToken,
                            $"Syntax Error: Invalid assignment target at {Current.Span}",
                            Current.Span);
                        throw new ParseException();
                    }

                    Consume(TokenType.Assign);
                    var value = ParseExpression();
                    var span = CreateSpan(expr.Span!.Start, value.Span!.End);

                    return new TargetAssignmentStatementNode(expr, value, span);
                }

                return new ExpressionStatementNode(
                    expr,
                    expr.Span);
            }
        }
    }

    private StatementNode ParseForStatement()
    {
        var start = Consume(TokenType.KeywordFor);

        // for item in iterable { ... }
        if (Current.Type == TokenType.Identifier && Next.Type == TokenType.KeywordIn)
        {
            var variableName = Consume(TokenType.Identifier).Value;
            Consume(TokenType.KeywordIn);
            var iterable = ParseExpression();
            var loopBody = ParseBlock(out var loopBodySpan);

            return new ForInStatementNode(
                variableName,
                iterable,
                loopBody,
                CreateSpan(start.Span.Start, loopBodySpan.End));
        }

        StatementNode? initializer = null;

        if (Current.Type != TokenType.Semicolon)
        {
            initializer = IsVariableDeclaration()
                ? ParseVariableDeclaration()
                : Current.Type == TokenType.Identifier && Next.Type == TokenType.Assign
                    ? ParseAssignmentStatement()
                    : ParseExpressionStatement();
        }

        Consume(TokenType.Semicolon);

        ExpressionNode? condition = null;

        if (Current.Type != TokenType.Semicolon)
        {
            condition = ParseExpression();
        }

        Consume(TokenType.Semicolon);

        StatementNode? update = null;

        if (Current.Type != TokenType.LBrace)
        {
            update = Current.Type == TokenType.Identifier &&
                     Next.Type == TokenType.Assign
                ? ParseAssignmentStatement()
                : ParseExpressionStatement();
        }

        var body = ParseBlock(out var bodySpan);

        var span = CreateSpan(
            start.Span.Start,
            bodySpan.End);

        return new ForStatementNode(
            initializer,
            condition,
            update,
            body,
            span);
    }

    private static bool IsTypeToken(TokenType type)
    {
        return type is
            TokenType.TypeI8 or
            TokenType.TypeI16 or
            TokenType.TypeI32 or
            TokenType.TypeI64 or
            TokenType.TypeU8 or
            TokenType.TypeU16 or
            TokenType.TypeU32 or
            TokenType.TypeU64 or
            TokenType.TypeUsize or
            TokenType.TypeF16 or
            TokenType.TypeF32 or
            TokenType.TypeF64 or
            TokenType.TypeBool or
            TokenType.TypeChar;
    }

    private DeferStatementNode ParseDeferStatement()
    {
        var deferToken = Consume(TokenType.KeywordDefer);

        if (Current.Type == TokenType.LBrace)
        {
            var block = ParseBlock(out var blockSpan);
            return new DeferStatementNode(block, CreateSpan(deferToken.Span.Start, blockSpan.End));
        }

        var statement = ParseStatement();
        return new DeferStatementNode([statement], CreateSpan(deferToken.Span.Start, statement.Span.End));
    }

    private SwitchStatementNode ParseSwitchStatement()
    {
        var start = Consume(TokenType.KeywordSwitch);
        var subject = ParseExpression();
        Consume(TokenType.LBrace);

        var arms = new List<SwitchArmNode>();
        IReadOnlyList<StatementNode>? defaultBody = null;

        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EndOfFile)
        {
            var armStart = Current;

            if (Current.Type == TokenType.KeywordDefault)
            {
                Advance();
                Consume(TokenType.FatArrow);
                var body = ParseSwitchArmBody();

                if (defaultBody is not null)
                {
                    Report(DiagnosticCodes.InvalidSwitch,
                        "A switch can have only one 'default' arm.",
                        armStart.Span);
                }

                defaultBody = body;
                continue;
            }

            var patterns = new List<ExpressionNode>();
            _inSwitchPattern = true;
            try
            {
                patterns.Add(ParseExpression());
                while (Current.Type == TokenType.Comma)
                {
                    Advance();
                    patterns.Add(ParseExpression());
                }
            }
            finally
            {
                _inSwitchPattern = false;
            }

            Consume(TokenType.FatArrow);
            var armBody = ParseSwitchArmBody();

            arms.Add(new SwitchArmNode(
                patterns,
                armBody,
                CreateSpan(armStart.Span.Start, Peek(-1).Span.End)));
        }

        var end = Consume(TokenType.RBrace);

        return new SwitchStatementNode(
            subject,
            arms,
            defaultBody,
            CreateSpan(start.Span.Start, end.Span.End));
    }

    // An arm body is a block or a single statement.
    private IReadOnlyList<StatementNode> ParseSwitchArmBody()
    {
        if (Current.Type == TokenType.LBrace)
        {
            return ParseBlock(out _);
        }

        return [ParseStatement()];
    }

    private WhileStatementNode ParseWhileStatement()
    {
        var start = Consume(TokenType.KeywordWhile);

        var condition = ParseExpression();
        var body = ParseBlock(out var bodySpan);

        var span = CreateSpan(
            start.Span.Start,
            bodySpan.End);

        return new WhileStatementNode(condition, body, span);
    }

    private IfStatementNode ParseIfStatement()
    {
        var start = Consume(TokenType.KeywordIf);

        var condition = ParseExpression();
        var thenBody = ParseBlock(out var thenSpan);

        IReadOnlyList<StatementNode>? elseBody = null;
        var end = thenSpan.End;

        if (Current.Type == TokenType.KeywordElse)
        {
            Consume(TokenType.KeywordElse);

            if (Current.Type == TokenType.KeywordIf)
            {
                var nestedIf = ParseIfStatement();

                elseBody = [nestedIf];
                end = nestedIf.Span!.End;
            }
            else
            {
                elseBody = ParseBlock(out var elseSpan);
                end = elseSpan.End;
            }
        }

        var span = CreateSpan(start.Span.Start, end);

        return new IfStatementNode(
            condition,
            thenBody,
            span, elseBody);
    }

    private IReadOnlyList<StatementNode> ParseBlock(out SourceSpan span)
    {
        var openingBrace = Consume(TokenType.LBrace);

        var statements = new List<StatementNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            statements.Add(ParseStatement());
        }

        var closingBrace = Consume(TokenType.RBrace);

        span = CreateSpan(
            openingBrace.Span.Start,
            closingBrace.Span.End);

        return statements;
    }

    private ExpressionNode ParseExpression(
        Precedence precedence = Precedence.Lowest)
    {
        var leftExpr = ParsePrefix();

        while (Current.Type != TokenType.EndOfFile)
        {
            if (Current.Type == TokenType.Dot)
            {
                leftExpr = ParseMemberAccess(leftExpr);

                // member( on the same line is a method call; on the next line it starts a statement.
                if (Current.Type == TokenType.LParen &&
                    Current.Span.Start.Line == Peek(-1).Span.End.Line)
                {
                    leftExpr = ParseMethodCall((MemberAccessNode)leftExpr);
                }
                else if (IsEnumStructBodyStart() &&
                         leftExpr is MemberAccessNode { Target: IdentifierNode enumIdentifier } variantAccess)
                {
                    // Enum.Variant { field: value }
                    var initializers = ParseFieldInitializers(out var closing);
                    leftExpr = new EnumStructLiteralNode(
                        enumIdentifier.Name,
                        variantAccess.MemberName,
                        initializers,
                        CreateSpan(variantAccess.Span!.Start, closing.Span.End));
                }

                continue;
            }

            // Postfix ++/-- must start on the line of its operand, otherwise it is the
            // prefix operator of the next statement (statements have no terminator).
            if (Current.Type is TokenType.PlusPlus or TokenType.MinusMinus &&
                Current.Span.Start.Line == Peek(-1).Span.End.Line)
            {
                leftExpr = ParsePostfixUpdate(leftExpr);
                continue;
            }

            // `expr as Type` binds tighter than any binary operator, but looser than a
            // prefix operator: `*p as u64` is `(*p) as u64`.
            if (Current.Type == TokenType.KeywordAs)
            {
                if (precedence >= Precedence.Cast)
                {
                    break;
                }

                leftExpr = ParseCast(leftExpr);
                continue;
            }

            if (Current.Type == TokenType.LBracket)
            {
                if (precedence >= Precedence.Postfix)
                {
                    break;
                }

                leftExpr = ParseIndexExpression(leftExpr);
                continue;
            }

            // `..` only appears inside `[start..end]`, which ParseIndexExpression handles.
            if (Current.Type == TokenType.DotDot)
            {
                break;
            }

            if (Current.Type == TokenType.Star && StartsDereferenceAssignment())
            {
                break;
            }

            if (Current.Type == TokenType.RParen ||
                Current.Type == TokenType.RBracket ||
                precedence >= GetPrecedence(Current.Type))
            {
                break;
            }

            leftExpr = ParseInfix(leftExpr);
        }

        return leftExpr;
    }

    // sizeof(Type) / alignof(Type)
    private ExpressionNode ParseTypeIntrinsic()
    {
        var keyword = Current;
        Advance();
        Consume(TokenType.LParen);
        var targetType = ParseTypeName();
        var closing = Consume(TokenType.RParen);
        var span = CreateSpan(keyword.Span.Start, closing.Span.End);

        return keyword.Type == TokenType.KeywordSizeOf
            ? new SizeOfExpressionNode(targetType, span)
            : new AlignOfExpressionNode(targetType, span);
    }

    private ExpressionNode ParseMethodCall(MemberAccessNode member)
    {
        Consume(TokenType.LParen);

        var arguments = new List<ExpressionNode>();

        while (Current.Type != TokenType.RParen && Current.Type != TokenType.EndOfFile)
        {
            arguments.Add(ParseExpression());

            if (Current.Type != TokenType.Comma)
            {
                break;
            }

            Consume(TokenType.Comma);
        }

        var closingParen = Consume(TokenType.RParen);

        return new MethodCallExpressionNode(
            member.Target,
            member.MemberName,
            arguments,
            CreateSpan(member.Span.Start, closingParen.Span.End));
    }

    private ExpressionNode ParseCast(ExpressionNode operand)
    {
        Consume(TokenType.KeywordAs);
        var targetType = ParseTypeName();

        return new CastExpressionNode(
            operand,
            targetType,
            CreateSpan(operand.Span.Start, Peek(-1).Span.End));
    }

    private ExpressionNode ParseIndexExpression(ExpressionNode target)
    {
        var start = target.Span.Start;
        Consume(TokenType.LBracket);

        // `[a..b]`, `[a..]`, `[..b]` and `[..]` slice a range; `[i]` indexes one element.
        ExpressionNode? first = null;
        if (Current.Type != TokenType.DotDot)
        {
            first = ParseExpression();
        }

        if (Current.Type == TokenType.DotDot)
        {
            Consume(TokenType.DotDot);

            ExpressionNode? last = null;
            if (Current.Type != TokenType.RBracket)
            {
                last = ParseExpression();
            }

            var rangeEnd = Consume(TokenType.RBracket);
            return new RangeIndexExpressionNode(target, first, last, CreateSpan(start, rangeEnd.Span.End));
        }

        var endToken = Consume(TokenType.RBracket);

        var span = CreateSpan(start, endToken.Span.End);
        return new IndexExpressionNode(target, first!, span);
    }

    private MemberAccessNode ParseMemberAccess(ExpressionNode target)
    {
        Consume(TokenType.Dot);

        var memberToken = Current;

        // t.0 and t.0.1: tuple element access. The lexer reads `0.1` as one number, so split it.
        if (memberToken.Type == TokenType.Number)
        {
            Advance();
            var indices = memberToken.Value.Split('.');
            if (indices.Any(part => part.Length == 0))
            {
                Report(DiagnosticCodes.UnexpectedToken,
                    $"Syntax Error: '{memberToken.Value}' is not a valid tuple index.", memberToken.Span);
                throw new ParseException();
            }

            MemberAccessNode access = null!;
            foreach (var index in indices)
            {
                access = new MemberAccessNode(
                    access ?? target,
                    index,
                    CreateSpan(target.Span!.Start, memberToken.Span.End));
            }

            return access;
        }

        var memberName = memberToken.Value;
        Consume(TokenType.Identifier);

        var span = CreateSpan(target.Span!.Start, memberToken.Span.End);
        return new MemberAccessNode(target, memberName, span);
    }

    private ExpressionStatementNode ParseExpressionStatement()
    {
        var expression = ParseExpression();

        return new ExpressionStatementNode(
            expression,
            expression.Span);
    }


    private ExpressionNode ParsePrefix()
    {
        switch (Current.Type)
        {
            case TokenType.LBracket:
                return ParseArrayLiteral();
            case TokenType.Number:
                return ParseNumberLiteral();
            case TokenType.Identifier:
                return ParseIdentifier();
            case TokenType.Character:
                return ParseCharLiteral();
            case TokenType.String:
                return ParseStringLiteral();
            case TokenType.KeywordSizeOf:
            case TokenType.KeywordAlignOf:
                return ParseTypeIntrinsic();
            case TokenType.KeywordNil:
                return new NilLiteralNode(Consume(TokenType.KeywordNil).Span);
            case TokenType.BooleanLiteral:
                return ParseBooleanLiteral();
            case TokenType.LParen:
                return ParseGroupedExpression();
            case TokenType.PlusPlus:
            case TokenType.MinusMinus:
                return ParsePrefixUpdate();
            case TokenType.Star:
                return ParseDereference();
            case TokenType.BitwiseAnd:
                return ParseAddressOf();
            case TokenType.LogicalNot:
            case TokenType.BitwiseNot:
            case TokenType.LeftShift:
            case TokenType.RightShift:
            case TokenType.Minus:
                return ParseUnaryExpression();
            default:
                Report(
                    DiagnosticCodes.UnexpectedToken,
                    $"Syntax Error: Unexpected prefix token {Current.Type}",
                    Current.Span);
                throw new ParseException();
        }
    }

    private ExpressionNode ParseGroupedExpression()
    {
        var open = Consume(TokenType.LParen);

        var expression = ParseExpression();

        if (Current.Type == TokenType.Comma)
        {
            // (a, b, ...) is a tuple; (a) is only grouping.
            var elements = new List<ExpressionNode> { expression };
            while (Current.Type == TokenType.Comma)
            {
                Consume(TokenType.Comma);
                if (Current.Type == TokenType.RParen)
                {
                    break;
                }

                elements.Add(ParseExpression());
            }

            var close = Consume(TokenType.RParen);
            return new TupleLiteralNode(elements, CreateSpan(open.Span.Start, close.Span.End));
        }

        Consume(TokenType.RParen);

        return expression;
    }

    private ExpressionNode ParseArrayLiteral()
    {
        var startToken = Consume(TokenType.LBracket);
        var elements = new List<ExpressionNode>();

        if (Current.Type != TokenType.RBracket)
        {
            while (true)
            {
                elements.Add(ParseExpression());

                if (Current.Type != TokenType.Comma)
                {
                    break;
                }

                Consume(TokenType.Comma);
            }
        }

        var endToken = Consume(TokenType.RBracket);
        var span = CreateSpan(startToken.Span.Start, endToken.Span.End);

        return new ArrayLiteralNode(elements, span);
    }

    private ExpressionNode ParseCharLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<char>(
            token.Value[0],
            token.Span);
    }

    private ExpressionNode ParseStringLiteral()
    {
        var token = Current;
        Advance();

        return new StringLiteralNode(token.Value, token.Span);
    }

    private ExpressionNode ParseDereference()
    {
        var operatorToken = Consume(TokenType.Star);
        var operand = ParseExpression(Precedence.Prefix);

        return new DereferenceExpressionNode(
            operand,
            CreateSpan(operatorToken.Span.Start, operand.Span.End));
    }

    private ExpressionNode ParseAddressOf()
    {
        var operatorToken = Consume(TokenType.BitwiseAnd);
        var operand = ParseExpression(Precedence.Prefix);

        return new AddressOfExpressionNode(
            operand,
            CreateSpan(operatorToken.Span.Start, operand.Span.End));
    }

    private ExpressionNode ParsePrefixUpdate()
    {
        var operatorToken = Current;
        Advance();

        // Binds tighter than any binary operator, but still allows a.b / a[i] operands.
        var target = ParseExpression(Precedence.Prefix);

        var span = CreateSpan(operatorToken.Span.Start, target.Span.End);

        return new UpdateExpressionNode(operatorToken.Value, target, true, span);
    }

    private ExpressionNode ParsePostfixUpdate(ExpressionNode target)
    {
        var operatorToken = Current;
        Advance();

        var span = CreateSpan(target.Span.Start, operatorToken.Span.End);

        return new UpdateExpressionNode(operatorToken.Value, target, false, span);
    }

    private ExpressionNode ParseUnaryExpression()
    {
        var operatorToken = Current;
        var op = operatorToken.Value;

        Advance();

        var operand = ParseExpression(Precedence.Prefix);

        var span = CreateSpan(
            operatorToken.Span.Start,
            operand.Span!.End);

        return new UnaryExpressionNode(op, operand, span);
    }

    private ExpressionNode ParseBooleanLiteral()
    {
        var token = Current;
        Advance();

        return new LiteralNode<bool>(
            token.Value.Equals("true", StringComparison.InvariantCulture),
            token.Span);
    }

    private ExpressionNode ParseInfix(ExpressionNode left)
    {
        var operatorToken = Current;
        var op = operatorToken.Value;
        var currentPrecedence = GetPrecedence(Current.Type);

        Advance();

        var right = ParseExpression(currentPrecedence);

        var span = CreateSpan(
            left.Span!.Start,
            right.Span!.End);

        return new BinaryExpressionNode(left, op, right, span);
    }


    private ExpressionNode ParseNumberLiteral()
    {
        var token = Current;
        var value = token.Value;

        Advance();

        if (value.Contains('.'))
        {
            if (!double.TryParse(
                    value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var decimalValue))
            {
                Report(
                    DiagnosticCodes.InvalidLiteral,
                    $"Syntax Error: Invalid decimal literal '{value}'",
                    token.Span);
                throw new ParseException();
            }

            return new LiteralNode<double>(
                decimalValue,
                token.Span);
        }

        return new LiteralNode<string>(
            value,
            token.Span);
    }

    private ExpressionNode ParseIdentifier()
    {
        var startToken = Current;
        var name = startToken.Value;
        Advance();

        while (Current.Type == TokenType.DoubleColon)
        {
            Consume(TokenType.DoubleColon);
            name += "::" + Consume(TokenType.Identifier).Value;
        }

        // `step<Vec2>(...)`, `Box<i32> { ... }`, `Box<i32>.new(...)`
        if (Current.Type == TokenType.LessThan && IsGenericName(name))
        {
            var scanEnd = ScanTypeArguments(0);
            if (scanEnd >= 0 && Peek(scanEnd).Type is TokenType.LParen or TokenType.LBrace or TokenType.Dot)
            {
                name += ParseTypeArguments();
            }
        }

        // `Name {` only starts a struct literal when followed by `field:`, so
        // `for ...; ++i { }` and `if flag { ... }` still open a block.
        if (Current.Type == TokenType.LBrace &&
            Peek(1).Type == TokenType.Identifier &&
            Peek(2).Type == TokenType.Colon)
        {
            return ParseStructLiteral(startToken with { Value = name });
        }

        // A `(` on the next line starts a new statement, e.g. `(*p).x = 1`, not a call.
        if (Current.Type != TokenType.LParen ||
            Current.Span.Start.Line != Peek(-1).Span.End.Line)
        {
            return new IdentifierNode(name, CreateSpan(startToken.Span.Start, Peek(-1).Span.End));
        }

        Consume(TokenType.LParen);

        var arguments = new List<ExpressionNode>();

        if (Current.Type != TokenType.RParen)
        {
            while (true)
            {
                arguments.Add(ParseExpression());

                if (Current.Type != TokenType.Comma)
                {
                    break;
                }

                Consume(TokenType.Comma);
            }
        }

        var closingParen = Consume(TokenType.RParen);

        var span = CreateSpan(
            startToken.Span.Start,
            closingParen.Span.End);

        return new FunctionCallExpressionNode(name, arguments, span);
    }

    private void RejectLetTypeAnnotation()
    {
        if (Current.Type != TokenType.Colon)
        {
            return;
        }

        Report(
            DiagnosticCodes.ExpectedToken,
            "Syntax Error: 'let' infers the type and cannot have a ': Type' annotation. " +
            "Write 'let x = expr', or declare the type with '<Type> <name> = expr' (for example 'u8 x = 10').",
            Current.Span);
        throw new ParseException();
    }

    private VariableDeclarationNode ParseVariableDeclaration()
    {
        var start = Current.Span.Start;

        // `let name = expr` infers the type (TypeConst.Let marks it for the type checker);
        // typed declarations are written `Type name = expr`, never `let name: Type`.
        var isLet = Current.Type == TokenType.KeywordLet;
        var type = isLet ? Consume(TokenType.KeywordLet).Value : ParseTypeName();

        var nameToken = Current;
        var name = nameToken.Value;
        Consume(TokenType.Identifier);

        if (isLet)
        {
            RejectLetTypeAnnotation();
        }

        ExpressionNode? initializer = null;
        var end = nameToken.Span.End;

        if (isLet && Current.Type != TokenType.Assign)
        {
            Report(
                DiagnosticCodes.ExpectedToken,
                $"Syntax Error: 'let {name}' needs an initializer.",
                Current.Span);
            throw new ParseException();
        }

        if (Current.Type == TokenType.Assign)
        {
            Consume(TokenType.Assign);
            initializer = ParseExpression();
            end = initializer.Span!.End;
        }

        var span = CreateSpan(start, end);

        return new VariableDeclarationNode(
            type,
            name,
            initializer,
            span);
    }

    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var start = Current.Span.Start;

        var name = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.Assign);

        var value = ParseExpression();

        var span = CreateSpan(start, value.Span!.End);

        return new AssignmentStatementNode(name, value, span);
    }

    private StatementNode ParseTargetAssignmentStatement()
    {
        var start = Current.Span.Start;
        var target = ParseExpression();

        // `arr[0]++` / `p.x--` / `a.method()` start like an assignment target but are plain statements.
        if (target is UpdateExpressionNode or MethodCallExpressionNode && Current.Type != TokenType.Assign)
        {
            return new ExpressionStatementNode(target, target.Span);
        }

        Consume(TokenType.Assign);

        var value = ParseExpression();
        var span = CreateSpan(start, value.Span!.End);

        return new TargetAssignmentStatementNode(target, value, span);
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
