using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
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
}
