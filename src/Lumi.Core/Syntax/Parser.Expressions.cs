using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
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
}
