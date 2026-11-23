namespace Lumi.Core.Syntax;

public enum TokenType
{
    KeywordStruct,
    KeywordFunc,
    KeywordReturn,
    KeywordIf,
    KeywordFor,
    KeywordWhile,
    KeywordBreak,
    KeywordContinue,
    KeywordElse,
    KeywordImport,
    KeywordModule,
    PrintFunc,

    TypeByte,
    TypeBool,
    TypeVoid,
    TypeChar,
    TypeI8,
    TypeI16,
    TypeI32,
    TypeI64,

    TypeU8,
    TypeU16,
    TypeU32,
    TypeU64,

    TypeF16,
    TypeF32,
    TypeF64,

    Identifier,
    Number,
    Character, // Char literal
    String, // String literal (value holds the unescaped text)
    BooleanLiteral,

    Assign,
    Plus,
    Star,
    Slash,
    Minus,
    Arrow,
    Comma,
    Dot,
    Colon,
    DoubleColon,
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

    //Bitwise Operators
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    BitwiseNot,
    LeftShift,
    RightShift,

    //Special
    EndOfFile,
    Unknown
}
