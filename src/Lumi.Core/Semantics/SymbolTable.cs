using Lumi.Core.Syntax;

namespace Lumi.Core.Semantics;

public sealed class SymbolTable(SymbolTable? parent = null)
{
    private readonly SymbolTable? _parent = parent;
    private readonly Dictionary<string, string> _symbols = new();

    /// <summary>
    ///     Declares a variable in the current scope.
    ///     Returns false if the variable is already declared in this scope.
    /// </summary>
    public bool TryDeclare(string name, string type)
    {
        return _symbols.TryAdd(name, type);
    }

    /// <summary>
    ///     Looks up a variable in the current scope and its parent scopes.
    ///     Returns false if the variable does not exist.
    /// </summary>
    public bool TryLookup(string name, out string type)
    {
        if (_symbols.TryGetValue(name, out type!))
        {
            return true;
        }

        if (_parent is not null)
        {
            return _parent.TryLookup(name, out type);
        }

        type = TypeConst.Error;
        return false;
    }
}
