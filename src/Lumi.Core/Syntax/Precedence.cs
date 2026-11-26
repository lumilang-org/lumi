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
    Range = 7,
    Shift = 8,
    Sum = 9,
    Product = 10,
    Cast = 11,
    Prefix = 12,
    Postfix = 13
}
