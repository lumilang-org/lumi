namespace Lumi.LLVM.CodeGenerator;

public sealed class VariableScope
{
    private readonly Dictionary<string, VariableSymbol> _variables = new();

    public VariableScope()
    {
    }

    private VariableScope(VariableScope parent)
    {
        Parent = parent;
    }

    public VariableScope? Parent { get; }

    public VariableScope EnterScope()
    {
        return new VariableScope(this);
    }

    public void Clear()
    {
        _variables.Clear();
    }

    public VariableScope ExitScope()
    {
        return Parent
               ?? throw new InvalidOperationException(
                   "Cannot exit the root scope.");
    }

    public void Declare(string name, VariableSymbol variable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(variable);

        if (!_variables.TryAdd(name, variable))
        {
            throw new InvalidOperationException(
                $"Compiler Error: Variable '{name}' is already declared in this scope.");
        }
    }

    public VariableSymbol ResolveVariable(string name)
    {
        if (_variables.TryGetValue(name, out var variable))
        {
            return variable;
        }

        if (Parent is not null)
        {
            return Parent.ResolveVariable(name);
        }

        throw new InvalidOperationException(
            $"Compiler Error: Undefined identifier '{name}' reached backend.");
    }
}
