namespace Lumi.Core.Semantics;

public enum CallReceiver
{
    // Struct.function(args): no receiver argument.
    None,

    // The receiver already has the type of the self parameter.
    Value,

    // A struct value passed to a *S self: its address is taken.
    AddressOf,

    // A *S pointer passed to an S self: it is dereferenced.
    Dereference
}
