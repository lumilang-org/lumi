namespace Lumi.Core.Syntax;

public enum TokenType
{
    KeywordStruct,
    KeywordFunc,
    KeywordReturn,
    PrintFunc,

    TypeByte,
    TypeBool,
    TypeVoid,
    TypeChar,
    TypeI16,
    TypeI32,
    TypeI64,

    TypeU16,
    TypeU32,
    TypeU64,

    TypeF16,
    TypeF32,
    TypeF64,

    Identifier,
    Number,
    Character, // Char literal
    BooleanLiteral,

    Assign,
    Plus,
    Star,
    Slash,
    Minus,
    Arrow,
    Comma,
    Semicolon,
    LBrace,
    RBrace,
    LParen,
    RParen,
    LBracket,
    RBracket,

    // Logical Operators
    EqualEqual,
    BangEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    LogicalAnd,
    LogicalOr,
    LogicalNot,

    //Special
    EndOfFile,
    Unknown
}