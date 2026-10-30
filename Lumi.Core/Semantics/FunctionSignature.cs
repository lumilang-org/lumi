namespace Lumi.Core.Semantics;

public sealed record FunctionSignature(
    string Name,
    IReadOnlyList<string> ParameterTypes,
    string ReturnType);
