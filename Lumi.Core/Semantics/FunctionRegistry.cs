namespace Lumi.Core.Semantics;

public sealed class FunctionRegistry
{
    private readonly Dictionary<string, FunctionSignature> _functions = [];

    public void Register(FunctionSignature signature)
    {
        if (!_functions.TryAdd(signature.Name, signature))
        {
            throw new Exception(
                $"Function '{signature.Name}' is already declared.");
        }
    }

    public FunctionSignature Get(string name)
    {
        if (!_functions.TryGetValue(name, out var signature))
        {
            throw new Exception(
                $"Function '{name}' is not declared.");
        }

        return signature;
    }

    public bool Contains(string name)
    {
        return _functions.ContainsKey(name);
    }
}
