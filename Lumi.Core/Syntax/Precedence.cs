namespace Lumi.Core.Syntax;

public enum Precedence
{
    Lowest = 0,
    LogicalOr = 1,
    LogicalAnd = 2,
    Comparison = 3,
    Sum = 4,
    Product = 5,
    Prefix = 6
}
