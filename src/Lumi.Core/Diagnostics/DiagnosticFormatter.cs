namespace Lumi.Core.Diagnostics;

public static class DiagnosticFormatter
{
    public static string Format(Diagnostic diagnostic)
    {
        var severity = diagnostic.Severity.ToString().ToLowerInvariant();

        var file = diagnostic.FileName;

        if (diagnostic.Span is null)
        {
            return $"{file}: {severity} {diagnostic.Code}: {diagnostic.Message}";
        }

        var location = diagnostic.Span.Start;

        return $"{file}:{location.Line}:{location.Column}: " +
               $"{severity} {diagnostic.Code}: {diagnostic.Message}";
    }
}
