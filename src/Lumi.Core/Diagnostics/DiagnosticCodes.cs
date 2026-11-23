namespace Lumi.Core.Diagnostics;

public static class DiagnosticCodes
{
    // Type checking
    public const string VoidExpression = "LUMI1001";
    public const string TypeMismatch = "LUMI1002";
    public const string UndefinedVariable = "LUMI1003";
    public const string UndefinedFunction = "LUMI1004";
    public const string ArgumentCountMismatch = "LUMI1005";

    // Parser errors
    public const string UnexpectedToken = "LUMI4001";
    public const string ExpectedToken = "LUMI4002";
    public const string ExpectedExpression = "LUMI4003";
    public const string InvalidLiteral = "LUMI4004";
    public const string ExpectedParameterType = "LUMI4005";
    public const string EmptyPrintExpression = "LUMI4006";

    // Function validation
    public const string MissingReturn = "LUMI2001";
    public const string InvalidReturn = "LUMI2002";

    // Control flow
    public const string BreakOutsideLoop = "LUMI3001";
    public const string ContinueOutsideLoop = "LUMI3002";

    public const string DuplicateFunction = "LUMI1006";
    public const string DuplicateVariable = "LUMI1007";
    public const string InvalidCondition = "LUMI1008";
    public const string InvalidOperator = "LUMI1009";
    public const string InvalidShiftAmount = "LUMI1010";
    public const string InvalidArrayLength = "LUMI1011";
    public const string IndexOutOfBounds = "LUMI1012";
    public const string CircularImport = "LUMI5001";
    public const string ModuleNotFound = "LUMI5002";

    // Lexer errors
    public const string UnterminatedString = "LUMI6001";
    public const string InvalidEscapeSequence = "LUMI6002";
    public const string EmptyCharacter = "LUMI6003";
    public const string MultiCharacter = "LUMI6004";
    public const string UnterminatedCharacter = "LUMI6005";
}
