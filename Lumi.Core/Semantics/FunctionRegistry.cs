namespace Lumi.Core.Semantics;

public sealed class FunctionRegistry
{
    private readonly Dictionary<string, FunctionSignature> _functions = [];

    public bool TryRegister(FunctionSignature signature)
    {
        return _functions.TryAdd(signature.Name, signature);
    }

    public bool TryGet(string name, out FunctionSignature signature)
    {
        return _functions.TryGetValue(name, out signature!);
    }

    public bool Contains(string name)
    {
        return _functions.ContainsKey(name);
    }
}
