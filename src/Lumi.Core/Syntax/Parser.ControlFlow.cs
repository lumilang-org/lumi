using Lumi.Core.Ast;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
    // True while parsing a switch pattern, where `Enum.Variant { x, y }` may bind by field name.
    private bool _inSwitchPattern;

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
}
