using Lumi.Core.Ast;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Diagnostics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Syntax;

[TestFixture]
public class ParserTests
{
    private ProgramNode Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        var diagnostics = new DiagnosticBag();
        var parser = new Parser(tokens, diagnostics, "test.lumi");
        var program = parser.ParseProgram();

        if (diagnostics.HasErrors)
        {
            var errors = string.Join(", ", diagnostics.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
            throw new Exception($"Parsing failed with errors: {errors}");
        }

        return program;
    }

    [Test]
    public void Parse_VariableDeclaration_BasicTypes()
    {
        var source = "func main() -> void { i32 x = 10 }";
        var program = Parse(source);

        Assert.That(program.Functions, Has.Count.EqualTo(1));
        var func = program.Functions[0];
        var varDecl = func.Body[0] as VariableDeclarationNode;
        Assert.That(varDecl, Is.Not.Null);
        Assert.That(varDecl.Type, Is.EqualTo("i32"));
        Assert.That(varDecl.Name, Is.EqualTo("x"));
        Assert.That(varDecl.Initializer, Is.InstanceOf<LiteralNode<string>>());
    }

    [Test]
    public void Parse_ArrayLiteral_CommaSeparated()
    {
        var source = "func main() -> void { u32[2] arr = [1, 2] }";
        var program = Parse(source);

        Assert.That(program.Functions, Has.Count.EqualTo(1));
        var func = program.Functions[0];
        var varDecl = func.Body[0] as VariableDeclarationNode;
        Assert.That(varDecl.Initializer, Is.InstanceOf<ArrayLiteralNode>());
        var arrayLiteral = (ArrayLiteralNode)varDecl.Initializer;
        Assert.That(arrayLiteral.Elements, Has.Count.EqualTo(2));
    }

    [Test]
    public void Parse_StructDeclaration()
    {
        var source = "struct Point { i32 x i32 y }";
        var program = Parse(source);

        Assert.That(program.Structs, Has.Count.EqualTo(1));
        var structDecl = program.Structs[0];
        Assert.That(structDecl.Name, Is.EqualTo("Point"));
        Assert.That(structDecl.Fields, Has.Count.EqualTo(2));
    }

    [Test]
    public void Parse_TargetAssignment_ArrayAndStruct()
    {
        var source = "func main() -> void { points[0].x = 10 }";
        var program = Parse(source);

        var funcDecl = program.Functions[0];
        var body = funcDecl.Body;
        Assert.That(body, Has.Count.EqualTo(1));

        var assign = body[0] as TargetAssignmentStatementNode;
        Assert.That(assign, Is.Not.Null);
        Assert.That(assign.Target, Is.InstanceOf<MemberAccessNode>());

        var memberAccess = (MemberAccessNode)assign.Target;
        Assert.That(memberAccess.MemberName, Is.EqualTo("x"));
        Assert.That(memberAccess.Target, Is.InstanceOf<IndexExpressionNode>());
    }

    [Test]
    public void Parse_ModuleAndImports()
    {
        var source = @"
        module std::math
        import std::types
        import core::io

        func add(i32 a, i32 b) -> i32 { return a + b }
        ";
        var program = Parse(source);

        Assert.That(program.Module, Is.Not.Null);
        Assert.That(program.Module.PathSegments, Is.EquivalentTo(new[] { "std", "math" }));
        Assert.That(program.Imports, Has.Count.EqualTo(2));
        Assert.That(program.Imports[0].PathSegments, Is.EquivalentTo(new[] { "std", "types" }));
        Assert.That(program.Imports[1].PathSegments, Is.EquivalentTo(new[] { "core", "io" }));
        Assert.That(program.Functions, Has.Count.EqualTo(1));
    }
}
