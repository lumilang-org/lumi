namespace Lumi.Core.Diagnostics;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public bool HasErrors =>
        _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Report(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    public void Report(string code, string message, SourceSpan? span = null)
    {
        Report(new Diagnostic(code, DiagnosticSeverity.Error, message, span));
    }
}
