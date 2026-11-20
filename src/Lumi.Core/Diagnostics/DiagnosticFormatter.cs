namespace Lumi.Core.Diagnostics;

public static class DiagnosticFormatter
{
    public static string Format(Diagnostic diagnostic)
    {
        var severity = diagnostic.Severity.ToString().ToLowerInvariant();

        if (diagnostic.Span is null)
        {
            return $"{severity} {diagnostic.Code}: {diagnostic.Message}";
        }

        var location = diagnostic.Span.Start;

        return $"{location.Line}:{location.Column}: " +
               $"{severity} {diagnostic.Code}: {diagnostic.Message}";
    }
}
