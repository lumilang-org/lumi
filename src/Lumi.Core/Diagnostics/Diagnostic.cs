namespace Lumi.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record Diagnostic(
    string FileName,
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan? Span = null);
