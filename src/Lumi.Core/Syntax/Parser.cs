using System.Globalization;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed class Parser(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics, string fileName)
{
    private int _position;

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

            TokenType.Plus or TokenType.Minus
                => Precedence.Sum,

            TokenType.Star or TokenType.Slash
                => Precedence.Product,

            _ => Precedence.Lowest
        };
    }

    private FunctionDeclarationNode ParseFunction()
    {
        var start = Consume(TokenType.KeywordFunc);

        var funcName = Current.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.LParen);

        var parameters = ParseFunctionParameters();

        Consume(TokenType.RParen);
        Consume(TokenType.Arrow);

        if (Current.Type != TokenType.TypeVoid &&
            !IsTypeToken(Current.Type) &&
            Current.Type != TokenType.Identifier)
        {
            Report(
                DiagnosticCodes.ExpectedToken,
                $"Syntax Error: Expected return type, got {Current.Type} ('{Current.Value}') at position {_position}",
                Current.Span);
            throw new ParseException();
        }

        var returnType = Current.Type == TokenType.TypeVoid ? "void" : ParseTypeName();
        if (returnType == TypeConst.Void)
        {
            Advance();
        }

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
            span);
    }


    public ProgramNode ParseProgram()
    {
        ModuleDeclarationNode? module = null;
        var imports = new List<ImportDeclarationNode>();
        var functions = new List<FunctionDeclarationNode>();
        var structs = new List<StructDeclarationNode>();

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
                if (Current.Type == TokenType.KeywordStruct)
                {
                    structs.Add(ParseStructDeclaration());
                }
                else
                {
                    functions.Add(ParseFunction());
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

        return new ProgramNode(module, imports, functions, structs, span, fileName);
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
               Current.Type != TokenType.KeywordFunc)
        {
            Advance();
        }
    }

    private StructDeclarationNode ParseStructDeclaration()
    {
        var start = Consume(TokenType.KeywordStruct);

        var nameToken = Current;
        var structName = nameToken.Value;
        Consume(TokenType.Identifier);

        Consume(TokenType.LBrace);

        var fields = new List<StructFieldNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            if (!IsTypeToken(Current.Type) && Current.Type != TokenType.Identifier)
            {
                Report(
                    DiagnosticCodes.ExpectedToken,
                    $"Syntax Error: Expected field type, got {Current.Type} ('{Current.Value}') at position {_position}",
                    Current.Span);
                throw new ParseException();
            }

            var fieldTypeToken = Current;
            var fieldType = fieldTypeToken.Value;
            Advance();

            var fieldNameToken = Current;
            var fieldName = fieldNameToken.Value;
            Consume(TokenType.Identifier);

            var fieldSpan = CreateSpan(
                fieldTypeToken.Span.Start,
                fieldNameToken.Span.End);

            fields.Add(new StructFieldNode(fieldType, fieldName, fieldSpan));

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
            span);
    }

    private StructLiteralNode ParseStructLiteral(Token identifierToken)
    {
        var structName = identifierToken.Value;
        Consume(TokenType.LBrace);

        var initializers = new List<StructFieldInitNode>();

        while (Current.Type != TokenType.RBrace &&
               Current.Type != TokenType.EndOfFile)
        {
            var fieldToken = Current;
            var fieldName = fieldToken.Value;
            Consume(TokenType.Identifier);

            Consume(TokenType.Colon);

            var valueExpr = ParseExpression();

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

        var closingBrace = Consume(TokenType.RBrace);

        var span = CreateSpan(
            identifierToken.Span.Start,
            closingBrace.Span.End);

        return new StructLiteralNode(
            structName,
            initializers,
            span);
    }

    private string ParseTypeName()
    {
        if (!IsTypeToken(Current.Type) && Current.Type != TokenType.Identifier)
        {
            Report(
                DiagnosticCodes.ExpectedToken,
                $"Syntax Error: Expected type name, got {Current.Type} ('{Current.Value}') at position {_position}",
                Current.Span);
            throw new ParseException();
        }

        var typeName = Current.Value;
        Advance();

        while (Current.Type == TokenType.DoubleColon)
        {
            Consume(TokenType.DoubleColon);
            typeName += "::" + Consume(TokenType.Identifier).Value;
        }

        while (Current.Type == TokenType.LBracket)
        {
            Consume(TokenType.LBracket);
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
        if (IsTypeToken(Current.Type))
        {
            return true;
        }

        if (Current.Type != TokenType.Identifier)
        {
            return false;
        }

        // It's an identifier. Check if it's followed by another identifier (e.g., Vec p)
        // or a bracket (e.g., Vec[2] p) or double colon (e.g., std::Vec p)
        var offset = 1;
        while (Peek(offset).Type == TokenType.DoubleColon)
        {
            offset++; // Consume '::'
            if (Peek(offset).Type != TokenType.Identifier)
            {
                return false;
            }

            offset++; // Consume identifier
        }

        while (Peek(offset).Type == TokenType.LBracket)
        {
            offset++; // Consume '['
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

    private StatementNode ParseStatement()
    {
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
            case TokenType.KeywordIf:
                return ParseIfStatement();
            case TokenType.KeywordWhile:
                return ParseWhileStatement();
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
                    if (expr is not IdentifierNode and not MemberAccessNode)
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

    private ForStatementNode ParseForStatement()
    {
        var start = Consume(TokenType.KeywordFor);

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
            TokenType.TypeF16 or
            TokenType.TypeF32 or
            TokenType.TypeF64 or
            TokenType.TypeBool or
            TokenType.TypeChar;
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

            if (Current.Type == TokenType.LBracket)
            {
                if (precedence >= Precedence.Postfix)
                {
                    break;
                }

                leftExpr = ParseIndexExpression(leftExpr);
                continue;
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

    private ExpressionNode ParseIndexExpression(ExpressionNode target)
    {
        var start = target.Span.Start;
        Consume(TokenType.LBracket);
        var index = ParseExpression();
        var endToken = Consume(TokenType.RBracket);

        var span = CreateSpan(start, endToken.Span.End);
        return new IndexExpressionNode(target, index, span);
    }

    private MemberAccessNode ParseMemberAccess(ExpressionNode target)
    {
        Consume(TokenType.Dot);

        var memberToken = Current;
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
            case TokenType.BooleanLiteral:
                return ParseBooleanLiteral();
            case TokenType.LParen:
                return ParseGroupedExpression();
            case TokenType.PlusPlus:
            case TokenType.MinusMinus:
                return ParsePrefixUpdate();
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
        Consume(TokenType.LParen);

        var expression = ParseExpression();

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

        // `Name {` only starts a struct literal when followed by `field:`, so
        // `for ...; ++i { }` and `if flag { ... }` still open a block.
        if (Current.Type == TokenType.LBrace &&
            Peek(1).Type == TokenType.Identifier &&
            Peek(2).Type == TokenType.Colon)
        {
            return ParseStructLiteral(startToken with { Value = name });
        }

        if (Current.Type != TokenType.LParen)
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

    private VariableDeclarationNode ParseVariableDeclaration()
    {
        var start = Current.Span.Start;

        var type = ParseTypeName();

        var nameToken = Current;
        var name = nameToken.Value;
        Consume(TokenType.Identifier);

        ExpressionNode? initializer = null;
        var end = nameToken.Span.End;

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

        // `arr[0]++` / `p.x--` start like an assignment target but are plain statements.
        if (target is UpdateExpressionNode)
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
