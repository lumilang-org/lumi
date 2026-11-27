using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
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

    private ExpressionStatementNode ParseExpressionStatement()
    {
        var expression = ParseExpression();

        return new ExpressionStatementNode(
            expression,
            expression.Span);
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
}
