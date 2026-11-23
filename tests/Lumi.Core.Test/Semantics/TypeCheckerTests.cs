using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class TypeCheckerTests
{
    private (ProgramNode, SemanticModel, DiagnosticBag) Check(string source)
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

        var typeChecker = new TypeChecker(diagnostics);
        typeChecker.CheckProgram(program);

        return (program, typeChecker.SemanticModel, diagnostics);
    }

    [Test]
    public void Check_VariableDeclaration_TypeMismatch()
    {
        var source = "func main() -> void { i32 x = true }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Check_Array_InvalidLength()
    {
        // Zero length array is not allowed (LUMI1011)
        var source = "func main() -> void { i32[0] arr }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.InvalidArrayLength), Is.True);
    }

    [Test]
    public void Check_Array_ConstantIndexOutOfBounds()
    {
        var source = "func main() -> void { i32[2] arr = [1, 2] i32 x = arr[2] }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.IndexOutOfBounds), Is.True);
    }

    [Test]
    public void Check_Struct_FieldAccess_Valid()
    {
        var source = "struct Point { i32 x } func main() -> void { Point p = Point { x: 10 } i32 x = p.x }";
        var (_, semanticModel, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_Struct_MissingField()
    {
        var source = "struct Point { i32 x } func main() -> void { Point p = Point { x: 10 } i32 y = p.y }";
        var (_, _, diagnostics) = Check(source);

        Assert.That(diagnostics.HasErrors, Is.True);
    }

    [Test]
    public void Check_FunctionCall_StructArgument_Valid()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func add(Vec2 v) -> i32 { return v.x + v.y }
        func main() -> void {
            Vec2 myVec = Vec2 { x: 10, y: 20 }
            i32 result = add(myVec)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_FunctionCall_ArrayArgument_Valid()
    {
        var source = @"
        func sum(i32[2] arr) -> i32 { return arr[0] + arr[1] }
        func main() -> void {
            i32[2] myArr = [10, 20]
            i32 result = sum(myArr)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_ReturnStruct_Valid()
    {
        var source = @"
        struct Vec2 { i32 x i32 y }
        func createVec(i32 x, i32 y) -> Vec2 {
            return Vec2 { x: x, y: y }
        }
        func main() -> void {
            Vec2 v = createVec(1, 2)
        }";
        var (_, _, diagnostics) = Check(source);
        if (diagnostics.HasErrors)
        {
            foreach (var d in diagnostics.Diagnostics)
            {
                throw new Exception($"[DEBUG_LOG] {d.Code}: {d.Message}");
            }
        }

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_ReturnArray_Valid()
    {
        var source = @"
        func createArr() -> i32[2] {
            return [1, 2]
        }
        func main() -> void {
            i32[2] a = createArr()
        }";
        var (_, _, diagnostics) = Check(source);
        if (diagnostics.HasErrors)
        {
            foreach (var d in diagnostics.Diagnostics)
            {
                Console.WriteLine($"[DEBUG_LOG] {d.Code}: {d.Message}");
            }
        }

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_FunctionCall_ArrayLengthMismatch()
    {
        var source = @"
        func sum(i32[3] arr) -> i32 { return arr[0] + arr[1] }
        func main() -> void {
            i32[2] myArr = [10, 20]
            i32 result = sum(myArr)
        }";
        var (_, _, diagnostics) = Check(source);
        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.TypeMismatch), Is.True);
    }

    private static bool HasCode(DiagnosticBag diagnostics, string code)
    {
        return diagnostics.Diagnostics.Any(d => d.Code == code);
    }

    [Test]
    public void Check_String_VariableDeclarationAndAssignment_AreValid()
    {
        var (_, _, diagnostics) = Check("func main() -> void { string s = \"a\" s = \"b\" }");

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_String_LiteralExpressionIsTypedAsString()
    {
        var (program, model, _) = Check("func main() -> void { string s = \"a\" }");

        var decl = (VariableDeclarationNode)program.Functions[0].Body[0];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("string"));
    }

    [Test]
    public void Check_String_AssigningNonStringToString_IsMismatch()
    {
        var (_, _, diagnostics) = Check("func main() -> void { string s = 1 }");

        Assert.That(HasCode(diagnostics, DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Check_String_AssigningStringToNonString_IsMismatch()
    {
        var (_, _, diagnostics) = Check("func main() -> void { i32 x = \"a\" char c = \"b\" }");

        Assert.That(diagnostics.Diagnostics.Count(d => d.Code == DiagnosticCodes.TypeMismatch), Is.EqualTo(2));
    }

    [Test]
    public void Check_String_ParametersCallsAndReturns()
    {
        var (_, _, valid) = Check(@"
func id(string s) -> string { return s }
func main() -> void { string r = id(""x"") }");
        var (_, _, badArg) = Check(@"
func id(string s) -> string { return s }
func main() -> void { string r = id(1) }");
        var (_, _, badReturn) = Check("func f() -> string { return 1 }");

        Assert.That(valid.HasErrors, Is.False);
        Assert.That(HasCode(badArg, DiagnosticCodes.TypeMismatch), Is.True);
        Assert.That(HasCode(badReturn, DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Check_String_StructFields()
    {
        var (_, _, valid) = Check(@"
struct Person { string name }
func main() -> void { Person p = Person { name: ""Ada"" } string n = p.name }");
        var (_, _, invalid) = Check(@"
struct Person { string name }
func main() -> void { Person p = Person { name: 1 } }");

        Assert.That(valid.HasErrors, Is.False);
        Assert.That(HasCode(invalid, DiagnosticCodes.TypeMismatch), Is.True);
    }

    [Test]
    public void Check_String_Arrays()
    {
        var (_, _, valid) = Check("func main() -> void { string[2] a = [\"x\", \"y\"] string s = a[1] }");
        var (_, _, invalid) = Check("func main() -> void { string[2] a = [\"x\", 1] }");

        Assert.That(valid.HasErrors, Is.False);
        Assert.That(invalid.HasErrors, Is.True);
    }

    [Test]
    public void Check_String_UnsupportedOperators_AreRejected()
    {
        var (_, _, diagnostics) = Check("func main() -> void { string s = \"a\" + \"b\" }");

        Assert.That(diagnostics.HasErrors, Is.True);
    }

    private static int CountCode(DiagnosticBag diagnostics, string code)
    {
        return diagnostics.Diagnostics.Count(d => d.Code == code);
    }

    [Test]
    public void Check_CharLiteral_IsChar_AndComparesWithStringElements()
    {
        var (_, _, diagnostics) = Check(@"
func main() -> void {
    string s = ""abc""
    char c = s[0]
    bool a = s[1] == 'a'
    bool b = s[2] != '\0'
}");

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_StringIndex_ReturnsChar()
    {
        var (program, model, diagnostics) = Check(
            "func main() -> void { string s = \"abc\" i64 i = 1 char c = s[i] }");

        Assert.That(diagnostics.HasErrors, Is.False);
        var decl = (VariableDeclarationNode)program.Functions[0].Body[2];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("char"));
    }

    [Test]
    public void Check_StringIndex_Invalid()
    {
        var (_, _, badIndex) = Check("func main() -> void { string s = \"a\" char c = s[\"x\"] }");
        var (_, _, negative) = Check("func main() -> void { string s = \"a\" char c = s[-1] }");
        var (_, _, wrongTarget) = Check("func main() -> void { i32 x = 1 i32 y = x[0] }");
        var (_, _, notInteger) = Check("func main() -> void { string s = \"a\" i32 n = s[0] }");

        Assert.That(CountCode(badIndex, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(CountCode(negative, DiagnosticCodes.IndexOutOfBounds), Is.EqualTo(1));
        Assert.That(wrongTarget.HasErrors, Is.True);
        Assert.That(notInteger.HasErrors, Is.True);
    }

    [Test]
    public void Check_StringElement_CannotBeAssigned()
    {
        var (_, _, diagnostics) = Check("func main() -> void { string s = \"abc\" s[0] = 'x' }");

        Assert.That(CountCode(diagnostics, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Len_OnString_IsI32()
    {
        var (program, model, diagnostics) = Check(
            "func main() -> void { string s = \"abc\" i32 n = len(s) i32 m = len(\"x\") + 1 }");

        Assert.That(diagnostics.HasErrors, Is.False);
        var decl = (VariableDeclarationNode)program.Functions[0].Body[1];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("i32"));
    }

    [Test]
    public void Check_Len_InvalidUsage()
    {
        var (_, _, wrongType) = Check("func main() -> void { i32 n = len(5) }");
        var (_, _, noArgs) = Check("func main() -> void { i32 n = len() }");
        var (_, _, twoArgs) = Check("func main() -> void { i32 n = len(\"a\", \"b\") }");

        Assert.That(CountCode(wrongType, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(noArgs, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(CountCode(twoArgs, DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Len_UserDefinedFunctionTakesPrecedence()
    {
        var (_, _, diagnostics) = Check(@"
func len(i32 x) -> i32 { return x }
func main() -> void { i32 n = len(3) }");

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_StringEquality_YieldsBool()
    {
        var (_, _, diagnostics) = Check(@"
func main() -> void {
    string a = ""x""
    bool e = a == ""x""
    bool n = a != ""y""
    if (a == ""x"") { }
}");

        Assert.That(diagnostics.HasErrors, Is.False);
    }

    [Test]
    public void Check_StringComparison_RejectsInvalidOperators()
    {
        foreach (var op in new[] { "<", "<=", ">", ">=", "+", "-", "&" })
        {
            var (_, _, diagnostics) = Check(
                $"func main() -> void {{ string a = \"x\" bool r = a {op} \"y\" }}");

            Assert.That(diagnostics.HasErrors, Is.True, $"operator '{op}' should be rejected on strings");
        }
    }

    [Test]
    public void Check_StringEquality_RejectsMismatchedOperands()
    {
        var (_, _, withInt) = Check("func main() -> void { string a = \"x\" bool r = a == 1 }");
        var (_, _, withChar) = Check("func main() -> void { string a = \"x\" bool r = a == 'x' }");
        var (_, _, intWithString) = Check("func main() -> void { i32 a = 1 bool r = a != \"x\" }");

        Assert.That(CountCode(withInt, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(CountCode(withChar, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(CountCode(intWithString, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
    }

    [Test]
    public void Check_Byte_IsAliasOfU8()
    {
        var (program, _, diagnostics) = Check(@"
func inc(byte b) -> byte { return b + 1 }
func main() -> void { byte x = 255 u8 y = x byte z = y byte[2] arr = [1, 2] }");
        var (_, _, overflow) = Check("func main() -> void { byte x = 256 }");

        Assert.That(diagnostics.HasErrors, Is.False);
        Assert.That(program.Functions[0].ReturnType, Is.EqualTo("u8"));
        Assert.That(overflow.HasErrors, Is.True);
    }
}
