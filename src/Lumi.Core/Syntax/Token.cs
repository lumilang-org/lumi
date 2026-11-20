using Lumi.Core.Diagnostics;

namespace Lumi.Core.Syntax;

public record Token(TokenType Type, string Value, SourceSpan Span);
