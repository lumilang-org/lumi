namespace Lumi.Core.Semantics;

public sealed class SymbolTable(SymbolTable? parent = null)
{
    private readonly Dictionary<string, string> _symbols = new();
    private readonly SymbolTable? _parent = parent;

    public void Declare(string name, string type)
    {
        if (!_symbols.TryAdd(name, type))
        {
            throw new Exception($"Semantic Error: Variable '{name}' is already declared in this scope.");
        }
    }
    
    public string Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var type))
        {
            return type;
        }

        if (_parent != null)
        {
            return _parent.Lookup(name);
        }

        throw new Exception($"Semantic Error: Variable '{name}' does not exist in the current context.");
    }
}