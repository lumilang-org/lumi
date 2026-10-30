namespace Lumi.Core.Diagnostics;

public sealed record SourceLocation(
    int Line,
    int Column);

public sealed record SourceSpan(
    SourceLocation Start,
    SourceLocation End);
