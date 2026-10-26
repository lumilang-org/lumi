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
    
    Identifier,
    Number,
    BooleanLiteral,
    
    Assign,
    Plus,
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
    
    //Special
    EndOfFile,
    Unknown
    
}