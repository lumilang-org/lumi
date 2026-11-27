using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;

namespace Lumi.Core.Syntax;

public sealed partial class Parser
{
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
}
