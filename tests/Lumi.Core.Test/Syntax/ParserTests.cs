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
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics, "test.lumi");
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

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

    [Test]
    public void Parse_StringLiteral_ProducesStringLiteralNode()
    {
        var program = Parse("func main() -> void { string s = \"hi\\n\" }");

        var varDecl = (VariableDeclarationNode)program.Functions[0].Body[0];
        Assert.That(varDecl.Type, Is.EqualTo("string"));
        var literal = varDecl.Initializer as StringLiteralNode;
        Assert.That(literal, Is.Not.Null);
        Assert.That(literal.Value, Is.EqualTo("hi\n"));
        Assert.That(literal.Span.Start.Column, Is.EqualTo(34));
    }

    [Test]
    public void Parse_StringType_InParametersReturnsFieldsAndArrays()
    {
        var program = Parse(@"
struct Person { string name }
func greet(string who) -> string { return who }
func main() -> void { string[2] names = [""a"", ""b""] }");

        Assert.That(program.Structs[0].Fields[0].Type, Is.EqualTo("string"));
        Assert.That(program.Functions[0].Parameters[0].Type, Is.EqualTo("string"));
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo("string"));
        var names = (VariableDeclarationNode)program.Functions[1].Body[0];
        Assert.That(names.Type, Is.EqualTo("string[2]"));
        Assert.That(names.Initializer, Is.InstanceOf<ArrayLiteralNode>());
    }

    [Test]
    public void Parse_StringType_InForInitializer()
    {
        var program = Parse("func main() -> void { for string s = \"x\"; true; s = \"y\" { } }");

        var forStmt = (ForStatementNode)program.Functions[0].Body[0];
        Assert.That(forStmt.Initializer, Is.InstanceOf<VariableDeclarationNode>());
    }

    [Test]
    public void Parse_ModuleNamedString_IsAccepted()
    {
        var program = Parse("module std::string");

        Assert.That(program.Module!.PathSegments, Is.EqualTo(new[] { "std", "string" }));
    }

    private static ExpressionNode StatementExpression(ProgramNode program, int index)
    {
        return ((ExpressionStatementNode)program.Functions[0].Body[index]).Expression;
    }

    [TestCase("i++", "++", false)]
    [TestCase("i--", "--", false)]
    [TestCase("++i", "++", true)]
    [TestCase("--i", "--", true)]
    public void Parse_UpdateStatement_PrefixAndPostfix(string statement, string op, bool isPrefix)
    {
        var program = Parse($"func main() -> void {{ i32 i = 0\n {statement} }}");

        var update = StatementExpression(program, 1) as UpdateExpressionNode;
        Assert.That(update, Is.Not.Null);
        Assert.That(update.Operator, Is.EqualTo(op));
        Assert.That(update.IsPrefix, Is.EqualTo(isPrefix));
        Assert.That(update.IsPostfix, Is.EqualTo(!isPrefix));
        Assert.That(((IdentifierNode)update.Target).Name, Is.EqualTo("i"));
    }

    [Test]
    public void Parse_UpdateExpression_SpanCoversOperatorAndOperand()
    {
        var program = Parse("func main() -> void { i32 i = 0\n ++i\n i++ }");

        var prefix = (UpdateExpressionNode)StatementExpression(program, 1);
        var postfix = (UpdateExpressionNode)StatementExpression(program, 2);
        Assert.That(prefix.Span.End.Column - prefix.Span.Start.Column, Is.EqualTo(3));
        Assert.That(postfix.Span.End.Column - postfix.Span.Start.Column, Is.EqualTo(3));
    }

    [Test]
    public void Parse_UpdateAsSubExpression()
    {
        var program = Parse("func main() -> void { i32 i = 0\n i32 a = ++i\n i32 b = i--\n i32 c = a + i++ - --b }");
        var body = program.Functions[0].Body;

        var a = (UpdateExpressionNode)((VariableDeclarationNode)body[1]).Initializer!;
        var b = (UpdateExpressionNode)((VariableDeclarationNode)body[2]).Initializer!;
        Assert.That(a.IsPrefix, Is.True);
        Assert.That(b.IsPostfix, Is.True);

        // (a + i++) - (--b)
        var sub = (BinaryExpressionNode)((VariableDeclarationNode)body[3]).Initializer!;
        Assert.That(sub.Operator, Is.EqualTo("-"));
        Assert.That(((UpdateExpressionNode)sub.Right).IsPrefix, Is.True);
        var sum = (BinaryExpressionNode)sub.Left;
        Assert.That(((UpdateExpressionNode)sum.Right).IsPostfix, Is.True);
    }

    [Test]
    public void Parse_Update_OnIndexAndMemberTargets()
    {
        var program = Parse(@"
struct P { i32 x }
func main() -> void {
    i32[2] a = [1, 2]
    P p = P { x: 1 }
    a[0]++
    --p.x
    ++a[1]
    p.x--
}");
        var body = program.Functions[0].Body;

        Assert.That(((UpdateExpressionNode)StatementExpression(program, 2)).Target, Is.InstanceOf<IndexExpressionNode>());
        Assert.That(((UpdateExpressionNode)StatementExpression(program, 3)).Target, Is.InstanceOf<MemberAccessNode>());
        Assert.That(((UpdateExpressionNode)StatementExpression(program, 4)).Target, Is.InstanceOf<IndexExpressionNode>());
        Assert.That(((UpdateExpressionNode)StatementExpression(program, 5)).Target, Is.InstanceOf<MemberAccessNode>());
        Assert.That(body, Has.Count.EqualTo(6));
    }

    [Test]
    public void Parse_Update_InForLoopInitializerAndStep()
    {
        var program = Parse(@"
func main() -> void {
    i32 i = 0
    for i++; i < 3; ++i { }
    for i32 j = 0; j < 3; j++ { }
}");

        var first = (ForStatementNode)program.Functions[0].Body[1];
        var second = (ForStatementNode)program.Functions[0].Body[2];
        Assert.That(((ExpressionStatementNode)first.Update!).Expression, Is.InstanceOf<UpdateExpressionNode>());
        Assert.That(((ExpressionStatementNode)second.Update!).Expression, Is.InstanceOf<UpdateExpressionNode>());
        Assert.That(first.Body, Is.Empty);
    }

    [Test]
    public void Parse_PostfixUpdate_MustBeOnTheOperandLine()
    {
        // The second line starts a new statement with a prefix ++.
        var program = Parse("func main() -> void { i32 i = 0 i32 j = 0\n i32 k = j\n ++i }");
        var body = program.Functions[0].Body;

        Assert.That(body, Has.Count.EqualTo(4));
        Assert.That(((VariableDeclarationNode)body[2]).Initializer, Is.InstanceOf<IdentifierNode>());
        Assert.That(((UpdateExpressionNode)StatementExpression(program, 3)).IsPrefix, Is.True);
    }

    [Test]
    public void Parse_ExistingUnaryAndBinaryMinusAndPlus_AreUnchanged()
    {
        var program = Parse("func main() -> void { i32 a = 1 i32 b = -a i32 c = a - -b i32 d = a + b - c }");
        var body = program.Functions[0].Body;

        Assert.That(((VariableDeclarationNode)body[1]).Initializer, Is.InstanceOf<UnaryExpressionNode>());
        var c = (BinaryExpressionNode)((VariableDeclarationNode)body[2]).Initializer!;
        Assert.That(c.Right, Is.InstanceOf<UnaryExpressionNode>());
    }

    [Test]
    public void Parse_IdentifierBeforeBlock_IsNotAStructLiteral()
    {
        var program = Parse("func main() -> void { bool flag = true if flag { println(1) } while flag { break } }");

        Assert.That(program.Functions[0].Body[1], Is.InstanceOf<IfStatementNode>());
        Assert.That(program.Functions[0].Body[2], Is.InstanceOf<WhileStatementNode>());
    }

    [TestCase("*i32 p", "*i32")]
    [TestCase("**i32 p", "**i32")]
    [TestCase("*Vec p", "*Vec")]
    [TestCase("*std::Vec p", "*std::Vec")]
    [TestCase("*i32[3] p", "*i32[3]")]
    [TestCase("*(i32[3]) p", "*(i32[3])")]
    [TestCase("**(i32[3]) p", "**(i32[3])")]
    [TestCase("*(i32) p", "*i32")]
    public void Parse_PointerTypes_InVariableDeclarations(string declaration, string expectedType)
    {
        var program = Parse($"func main() -> void {{ {declaration} }}");

        var decl = (VariableDeclarationNode)program.Functions[0].Body[0];
        Assert.That(decl.Type, Is.EqualTo(expectedType));
        Assert.That(decl.Name, Is.EqualTo("p"));
    }

    [Test]
    public void Parse_PointerTypes_InParametersReturnsAndStructFields()
    {
        var program = Parse(@"
struct Node { i32 value *Node next i32[2] pair **i32 deep }
func first(*i32 a, **Node b) -> *i32 { return a }
func main() -> void { }");

        var fields = program.Structs[0].Fields;
        Assert.That(fields.Select(f => f.Type), Is.EqualTo(new[] { "i32", "*Node", "i32[2]", "**i32" }));
        Assert.That(program.Functions[0].Parameters.Select(p => p.Type), Is.EqualTo(new[] { "*i32", "**Node" }));
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo("*i32"));
    }

    [Test]
    public void Parse_AddressOfAndDereference_ProduceNodesWithSpans()
    {
        var program = Parse("func main() -> void { i32 x = 1\n *i32 p = &x i32 y = *p }");
        var body = program.Functions[0].Body;

        var addressOf = (AddressOfExpressionNode)((VariableDeclarationNode)body[1]).Initializer!;
        var dereference = (DereferenceExpressionNode)((VariableDeclarationNode)body[2]).Initializer!;
        Assert.That(((IdentifierNode)addressOf.Operand).Name, Is.EqualTo("x"));
        Assert.That(((IdentifierNode)dereference.Operand).Name, Is.EqualTo("p"));
        Assert.That(addressOf.Span.End.Column - addressOf.Span.Start.Column, Is.EqualTo(2));
        Assert.That(dereference.Span.End.Column - dereference.Span.Start.Column, Is.EqualTo(2));
        Assert.That(addressOf.Span.Start.Column, Is.EqualTo(11));
    }

    [Test]
    public void Parse_Dereference_AsAssignmentTarget()
    {
        var program = Parse("func main() -> void { i32 x = 1\n *i32 p = &x\n *p = 2\n **pp = 3\n (*p).y = 4 }");
        var body = program.Functions[0].Body;

        var simple = (TargetAssignmentStatementNode)body[2];
        var chained = (TargetAssignmentStatementNode)body[3];
        var member = (TargetAssignmentStatementNode)body[4];
        Assert.That(simple.Target, Is.InstanceOf<DereferenceExpressionNode>());
        Assert.That(((DereferenceExpressionNode)chained.Target).Operand, Is.InstanceOf<DereferenceExpressionNode>());
        Assert.That(member.Target, Is.InstanceOf<MemberAccessNode>());
        Assert.That(((MemberAccessNode)member.Target).Target, Is.InstanceOf<DereferenceExpressionNode>());
    }

    [Test]
    public void Parse_StarAndAmpersand_KeepBinaryMeaning_AndBindTighterThanBinaryOperators()
    {
        var program = Parse(@"
func main() -> void {
    i32 a = *p * 2
    i32 b = 2 * *p
    i32 c = *p + 1
    i32 d = a & b
    i32 e = a & &b
    i32 f = a * b
}");
        var body = program.Functions[0].Body;
        ExpressionNode Init(int i) => ((VariableDeclarationNode)body[i]).Initializer!;

        var a = (BinaryExpressionNode)Init(0);
        Assert.That(a.Operator, Is.EqualTo("*"));
        Assert.That(a.Left, Is.InstanceOf<DereferenceExpressionNode>());

        var b = (BinaryExpressionNode)Init(1);
        Assert.That(b.Right, Is.InstanceOf<DereferenceExpressionNode>());

        var c = (BinaryExpressionNode)Init(2);
        Assert.That(c.Operator, Is.EqualTo("+"));
        Assert.That(c.Left, Is.InstanceOf<DereferenceExpressionNode>());

        Assert.That(((BinaryExpressionNode)Init(3)).Operator, Is.EqualTo("&"));
        Assert.That(((BinaryExpressionNode)Init(4)).Right, Is.InstanceOf<AddressOfExpressionNode>());
        Assert.That(((BinaryExpressionNode)Init(5)).Operator, Is.EqualTo("*"));
    }

    [Test]
    public void Parse_AddressOfAndDereference_WithPostfixChains()
    {
        var program = Parse(@"
func main() -> void {
    *i32 a = &p.x
    *i32 b = &arr[0]
    i32 c = (*p).x
    i32 d = *p.next
    *i32 e = &(*p).x
}");
        var body = program.Functions[0].Body;
        ExpressionNode Init(int i) => ((VariableDeclarationNode)body[i]).Initializer!;

        Assert.That(((AddressOfExpressionNode)Init(0)).Operand, Is.InstanceOf<MemberAccessNode>());
        Assert.That(((AddressOfExpressionNode)Init(1)).Operand, Is.InstanceOf<IndexExpressionNode>());
        Assert.That(Init(2), Is.InstanceOf<MemberAccessNode>());
        // *p.next is *(p.next)
        Assert.That(((DereferenceExpressionNode)Init(3)).Operand, Is.InstanceOf<MemberAccessNode>());
        Assert.That(((MemberAccessNode)((AddressOfExpressionNode)Init(4)).Operand).Target,
            Is.InstanceOf<DereferenceExpressionNode>());
    }

    [Test]
    public void Parse_StatementBoundaries_AroundDereference()
    {
        var program = Parse(@"
func f(i32 x) -> void {
    i32 y = x
    *p = 1
    f(y)
    (*p).x = 2
    i32 z = y
        * 3
}");
        var body = program.Functions[0].Body;

        Assert.That(body, Has.Count.EqualTo(5));
        Assert.That(body[1], Is.InstanceOf<TargetAssignmentStatementNode>());
        Assert.That(body[2], Is.InstanceOf<ExpressionStatementNode>());
        Assert.That(body[3], Is.InstanceOf<TargetAssignmentStatementNode>());
        // A leading `*` without an assignment on its line still continues the expression.
        Assert.That(((VariableDeclarationNode)body[4]).Initializer, Is.InstanceOf<BinaryExpressionNode>());
    }

    [Test]
    public void Parse_PointerDeclarations_DoNotCaptureDereferenceStatements()
    {
        var program = Parse("func main() -> void { *i32 p = &x\n *p = 1 }");

        Assert.That(program.Functions[0].Body[0], Is.InstanceOf<VariableDeclarationNode>());
        Assert.That(program.Functions[0].Body[1], Is.InstanceOf<TargetAssignmentStatementNode>());
    }

    [Test]
    public void Parse_PointerToVoid_IsASyntaxError()
    {
        Assert.Throws<Exception>(() => Parse("func f(*void p) -> void { }"));
    }

    [Test]
    public void Parse_Usize_InEveryTypePosition()
    {
        var program = Parse(@"
struct Slice { usize start usize[2] bounds *usize cursor }
func length(usize a, *usize b) -> usize { return a }
func main() -> void {
    usize n = 1
    usize[3] sizes = [1, 2, 3]
    *usize p = &n
    for usize i = 0; i < 3; i++ { }
}");

        Assert.That(program.Structs[0].Fields.Select(f => f.Type),
            Is.EqualTo(new[] { "usize", "usize[2]", "*usize" }));
        Assert.That(program.Functions[0].Parameters.Select(p => p.Type), Is.EqualTo(new[] { "usize", "*usize" }));
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo("usize"));

        var body = program.Functions[1].Body;
        Assert.That(((VariableDeclarationNode)body[0]).Type, Is.EqualTo("usize"));
        Assert.That(((VariableDeclarationNode)body[1]).Type, Is.EqualTo("usize[3]"));
        Assert.That(((VariableDeclarationNode)body[2]).Type, Is.EqualTo("*usize"));
        var init = (VariableDeclarationNode)((ForStatementNode)body[3]).Initializer!;
        Assert.That(init.Type, Is.EqualTo("usize"));
    }

    [Test]
    public void Parse_Usize_DeclarationSpanCoversTypeAndInitializer()
    {
        var program = Parse("func main() -> void { usize n = 10 }");

        var decl = (VariableDeclarationNode)program.Functions[0].Body[0];
        Assert.That(decl.Span.Start.Column, Is.EqualTo(23));
        Assert.That(decl.Initializer!.Span.Start.Column, Is.EqualTo(33));
    }

    [Test]
    public void Parse_Usize_InPrintAndExpressions()
    {
        var program = Parse("func main() -> void { usize n = 1 print(n) println(n + 1) }");

        Assert.That(program.Functions[0].Body[1], Is.InstanceOf<PrintStatementNode>());
        Assert.That(((PrintStatementNode)program.Functions[0].Body[2]).AppendNewLine, Is.True);
    }

    [Test]
    public void Parse_Nil_ProducesNilLiteralNodeWithSpan()
    {
        var program = Parse("func main() -> void { *i32 p = nil }");

        var init = ((VariableDeclarationNode)program.Functions[0].Body[0]).Initializer;
        var nil = init as NilLiteralNode;
        Assert.That(nil, Is.Not.Null);
        Assert.That(nil.Span.Start.Column, Is.EqualTo(32));
        Assert.That(nil.Span.End.Column - nil.Span.Start.Column, Is.EqualTo(3));
    }

    [Test]
    public void Parse_Nil_InEveryExpressionPosition()
    {
        var program = Parse(@"
struct Node { i32 value *Node next }
func take(*i32 p) -> *i32 { return nil }
func main() -> void {
    *i32 p = nil
    p = nil
    *p = 1
    bool a = p == nil
    bool b = nil != p
    bool c = nil == nil
    take(nil)
    Node n = Node { value: 1, next: nil }
    n.next = nil
    *i32[2] arr = [nil, p]
}");
        var body = program.Functions[1].Body;

        Assert.That(((ReturnStatementNode)program.Functions[0].Body[0]).Expression, Is.InstanceOf<NilLiteralNode>());
        Assert.That(((AssignmentStatementNode)body[1]).Value, Is.InstanceOf<NilLiteralNode>());
        Assert.That(((BinaryExpressionNode)((VariableDeclarationNode)body[3]).Initializer!).Right,
            Is.InstanceOf<NilLiteralNode>());
        Assert.That(((BinaryExpressionNode)((VariableDeclarationNode)body[4]).Initializer!).Left,
            Is.InstanceOf<NilLiteralNode>());
        var both = (BinaryExpressionNode)((VariableDeclarationNode)body[5]).Initializer!;
        Assert.That(both.Left, Is.InstanceOf<NilLiteralNode>());
        Assert.That(both.Right, Is.InstanceOf<NilLiteralNode>());
        var call = (FunctionCallExpressionNode)((ExpressionStatementNode)body[6]).Expression;
        Assert.That(call.Arguments[0], Is.InstanceOf<NilLiteralNode>());
        var literal = (StructLiteralNode)((VariableDeclarationNode)body[7]).Initializer!;
        Assert.That(literal.Initializers[1].Value, Is.InstanceOf<NilLiteralNode>());
        Assert.That(((TargetAssignmentStatementNode)body[8]).Value, Is.InstanceOf<NilLiteralNode>());
        var array = (ArrayLiteralNode)((VariableDeclarationNode)body[9]).Initializer!;
        Assert.That(array.Elements[0], Is.InstanceOf<NilLiteralNode>());
    }

    [Test]
    public void Parse_NilBeforeABlock_DoesNotStartAStructLiteral()
    {
        var program = Parse("func main() -> void { *i32 p = nil\n if p == nil { println(1) } }");

        Assert.That(program.Functions[0].Body[1], Is.InstanceOf<IfStatementNode>());
    }

    [Test]
    public void Parse_Cast_PostfixOnCallsAndMembers()
    {
        var program = Parse(@"
struct S { i32 a }
func f() -> *u8 { return nil }
func main() -> void {
    *u64 a = f() as *u64
    *S b = f() as *S
    *u8 c = b.a as *u8
}");
        var body = program.Functions[1].Body;

        var first = ((VariableDeclarationNode)body[0]).Initializer as CastExpressionNode;
        Assert.That(first, Is.Not.Null);
        Assert.That(first!.TargetType, Is.EqualTo("*u64"));
        Assert.That(first.Operand, Is.InstanceOf<FunctionCallExpressionNode>());

        var second = (CastExpressionNode)((VariableDeclarationNode)body[1]).Initializer!;
        Assert.That(second.TargetType, Is.EqualTo("*S"));

        var third = (CastExpressionNode)((VariableDeclarationNode)body[2]).Initializer!;
        Assert.That(third.Operand, Is.InstanceOf<MemberAccessNode>());
    }

    [Test]
    public void Parse_Cast_DereferenceOfParenthesizedCast()
    {
        var program = Parse("func main() -> void { *u8 p = nil\n *(p as *u64) = 1000 }");

        var assignment = (TargetAssignmentStatementNode)program.Functions[0].Body[1];
        var target = (DereferenceExpressionNode)assignment.Target;
        Assert.That(target.Operand, Is.InstanceOf<CastExpressionNode>());
    }

    [Test]
    public void Parse_Cast_BindsLooserThanPrefixTighterThanBinary()
    {
        var program = Parse("func main() -> void { *i32 p = nil\n u64 a = *p as u64\n u64 b = a + 1 as u64 * 2 }");
        var body = program.Functions[0].Body;

        var deref = (CastExpressionNode)((VariableDeclarationNode)body[1]).Initializer!;
        Assert.That(deref.Operand, Is.InstanceOf<DereferenceExpressionNode>());

        var sum = (BinaryExpressionNode)((VariableDeclarationNode)body[2]).Initializer!;
        Assert.That(sum.Right, Is.InstanceOf<BinaryExpressionNode>());
        Assert.That(((BinaryExpressionNode)sum.Right).Left, Is.InstanceOf<CastExpressionNode>());
    }

    [Test]
    public void Parse_Cast_Chains_AndAsIsNoLongerAnIdentifier()
    {
        var program = Parse("func main() -> void { u8 a = 1\n u64 b = a as u16 as u64 }");

        var outer = (CastExpressionNode)((VariableDeclarationNode)program.Functions[0].Body[1]).Initializer!;
        Assert.That(outer.Operand, Is.InstanceOf<CastExpressionNode>());
    }

    [Test]
    public void Parse_SizeOfAndAlignOf_AllTypeForms()
    {
        var program = Parse(@"
struct Pair { u64 a u64 b }
func main() -> usize {
    usize a = sizeof(u8)
    usize b = sizeof(*u8)
    usize c = alignof(Pair)
    usize d = sizeof(i32[4]) + alignof(u64) * 2
    return sizeof(Pair)
}");
        var body = program.Functions[0].Body;

        var a = (SizeOfExpressionNode)((VariableDeclarationNode)body[0]).Initializer!;
        Assert.That(a.TargetType, Is.EqualTo("u8"));
        Assert.That(((SizeOfExpressionNode)((VariableDeclarationNode)body[1]).Initializer!).TargetType, Is.EqualTo("*u8"));
        Assert.That(((AlignOfExpressionNode)((VariableDeclarationNode)body[2]).Initializer!).TargetType, Is.EqualTo("Pair"));

        var sum = (BinaryExpressionNode)((VariableDeclarationNode)body[3]).Initializer!;
        Assert.That(((SizeOfExpressionNode)sum.Left).TargetType, Is.EqualTo("i32[4]"));
        Assert.That(sum.Right, Is.InstanceOf<BinaryExpressionNode>());
        Assert.That(((ReturnStatementNode)body[4]).Expression, Is.InstanceOf<SizeOfExpressionNode>());
    }

    [Test]
    public void Parse_SizeOf_AsFunctionArgument()
    {
        var program = Parse("func main() -> void { f(&a, sizeof(Pair), alignof(Pair)) }");

        var call = (FunctionCallExpressionNode)((ExpressionStatementNode)program.Functions[0].Body[0]).Expression;
        Assert.That(call.Arguments[1], Is.InstanceOf<SizeOfExpressionNode>());
        Assert.That(call.Arguments[2], Is.InstanceOf<AlignOfExpressionNode>());
    }

    [Test]
    public void Parse_Let_InferredAndAnnotated()
    {
        var program = Parse("func main() -> void { let x = 20\n let y: i64 = 5\n let s = \"Sam\" }");
        var body = program.Functions[0].Body;

        var inferred = (VariableDeclarationNode)body[0];
        Assert.That(inferred.Type, Is.EqualTo(TypeConst.Let));
        Assert.That(inferred.Name, Is.EqualTo("x"));
        Assert.That(inferred.Initializer, Is.Not.Null);

        var annotated = (VariableDeclarationNode)body[1];
        Assert.That(annotated.Type, Is.EqualTo("i64"));
        Assert.That(annotated.Name, Is.EqualTo("y"));

        Assert.That(((VariableDeclarationNode)body[2]).Initializer, Is.InstanceOf<StringLiteralNode>());
    }

    [Test]
    public void Parse_Let_WithoutInitializer_IsASyntaxError()
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("func main() -> void { let x }", diagnostics, "test.lumi").Tokenize();
        new Parser(tokens, diagnostics, "test.lumi").ParseProgram();

        Assert.That(diagnostics.HasErrors, Is.True);
    }

    [Test]
    public void Parse_ForIn_ProducesForInNode_AndKeepsClassicFor()
    {
        var program = Parse(@"
func main() -> void {
    i32[3] arr = [1, 2, 3]
    for x in arr {
        println(x)
    }
    for y in [4, 5] { println(y) }
    for i32 j = 0; j < 2; j++ { println(j) }
}");
        var body = program.Functions[0].Body;

        var forIn = (ForInStatementNode)body[1];
        Assert.That(forIn.VariableName, Is.EqualTo("x"));
        Assert.That(forIn.Iterable, Is.InstanceOf<IdentifierNode>());
        Assert.That(forIn.Body, Has.Count.EqualTo(1));
        Assert.That(((ForInStatementNode)body[2]).Iterable, Is.InstanceOf<ArrayLiteralNode>());
        Assert.That(body[3], Is.InstanceOf<ForStatementNode>());
    }

    [Test]
    public void Parse_ForIn_WithoutInOrBody_IsASyntaxError()
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("func main() -> void { for x in { } }", diagnostics, "test.lumi").Tokenize();
        new Parser(tokens, diagnostics, "test.lumi").ParseProgram();

        Assert.That(diagnostics.HasErrors, Is.True);
    }
}
