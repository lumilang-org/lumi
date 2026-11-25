using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed record FunctionSignature(
    string Name,
    IReadOnlyList<string> ParameterTypes,
    string ReturnType,
    VisibilityModifier VisibilityModifier);
