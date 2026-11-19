namespace Lumi.Core.Syntax;

public enum Precedence
{
    Lowest = 0,
    LogicalOr = 1,
    LogicalAnd = 2,
    BitwiseOr = 3,
    BitwiseXor = 4,
    BitwiseAnd = 5,
    Comparison = 6,
    Shift = 7,
    Sum = 8,
    Product = 9,
    Prefix = 10,
    Postfix = 11
}
