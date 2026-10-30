namespace Lumi.Core.Diagnostics;

public static class DiagnosticCodes
{
    // Type checking
    public const string VoidExpression = "LUMI1001";
    public const string TypeMismatch = "LUMI1002";
    public const string UndefinedVariable = "LUMI1003";
    public const string UndefinedFunction = "LUMI1004";
    public const string ArgumentCountMismatch = "LUMI1005";

    // Function validation
    public const string MissingReturn = "LUMI2001";
    public const string InvalidReturn = "LUMI2002";

    // Control flow
    public const string BreakOutsideLoop = "LUMI3001";
    public const string ContinueOutsideLoop = "LUMI3002";
}
