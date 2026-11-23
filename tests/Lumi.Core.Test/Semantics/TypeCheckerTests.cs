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
    }

    [TestCase("i8")]
    [TestCase("i16")]
    [TestCase("i32")]
    [TestCase("i64")]
    [TestCase("u8")]
    [TestCase("u16")]
    [TestCase("u32")]
    [TestCase("u64")]
    [TestCase("byte")]
    public void Check_Update_OnIntegerTypes_KeepsOperandType(string type)
    {
        var expected = type == "byte" ? "u8" : type;
        var (program, model, diagnostics) = Check(
            $"func main() -> void {{ {type} x = 1 {type} a = x++ {type} b = --x }}");

        Assert.That(diagnostics.HasErrors, Is.False);
        var a = (VariableDeclarationNode)program.Functions[0].Body[1];
        var b = (VariableDeclarationNode)program.Functions[0].Body[2];
        Assert.That(model.GetExpressionType(a.Initializer!), Is.EqualTo(expected));
        Assert.That(model.GetExpressionType(b.Initializer!), Is.EqualTo(expected));
    }

    [Test]
    public void Check_Update_ValidTargets()
    {
        var (_, _, diagnostics) = Check(@"
struct Inner { i32 n }
struct Outer { Inner inner }
func bump(i32 p) -> void {
    p++
    ++p
}
func main() -> void {
    i32[3] arr = [1, 2, 3]
    Outer o = Outer { inner: Inner { n: 1 } }
    Inner[2] items = [Inner { n: 1 }, Inner { n: 2 }]
    arr[1]++
    --arr[0]
    o.inner.n++
    items[1].n--
    for i32 i = 0; i < 3; i++ { }
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
    }

    [Test]
    public void Check_Update_RejectsNonLvalues_WithFileAwareDiagnostic()
    {
        var sources = new[]
        {
            "func main() -> void { 1++ }",
            "func main() -> void { i32 x = 1 (x + 1)++ }",
            "func main() -> void { i32 x = 1\n --(x + 1) }",
            "func f() -> i32 { return 1 }\nfunc main() -> void { f()++ }",
            "func main() -> void { i32 x = 1\n ++(x++) }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.InvalidUpdateTarget), Is.EqualTo(1), source);
            var diagnostic = diagnostics.Diagnostics.First(d => d.Code == DiagnosticCodes.InvalidUpdateTarget);
            Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
            Assert.That(diagnostic.Span, Is.Not.Null);
        }
    }

    [Test]
    public void Check_Update_RejectsNonIntegerTypes()
    {
        var sources = new[]
        {
            "func main() -> void { bool b = true b++ }",
            "func main() -> void { string s = \"a\"\n ++s }",
            "func main() -> void { char c = 'a' c++ }",
            "func main() -> void { f32 f = 1.5 f-- }",
            "struct P { i32 x }\nfunc main() -> void { P p = P { x: 1 } p++ }",
            "func main() -> void { i32[2] a = [1, 2] a++ }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.InvalidOperator), Is.EqualTo(1), source);
        }
    }

    [Test]
    public void Check_Update_RejectsStringElementsAndUndefinedNames()
    {
        var (_, _, stringElement) = Check("func main() -> void { string s = \"abc\" s[0]++ }");
        var (_, _, undefined) = Check("func main() -> void { missing++ }");

        Assert.That(CountCode(stringElement, DiagnosticCodes.InvalidUpdateTarget), Is.EqualTo(1));
        Assert.That(CountCode(undefined, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_Update_ResultTypeFollowsAssignmentRules()
    {
        var (_, _, widening) = Check("func main() -> void { i32 x = 1 i64 y = x++ }");
        var (_, _, ok) = Check("func main() -> void { i32 x = 1 i32 y = x++ + ++x }");

        Assert.That(CountCode(widening, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(ok.HasErrors, Is.False);
    }

    [Test]
    public void Check_Pointers_AddressOfAndDereferenceTypes()
    {
        var (program, model, diagnostics) = Check(@"
struct P { i32 x }
func main() -> void {
    i32 x = 1
    P pt = P { x: 1 }
    i32[3] arr = [1, 2, 3]
    string s = ""abc""
    *i32 a = &x
    **i32 b = &a
    i32 c = **b
    *i32 d = &pt.x
    *i32 e = &arr[1]
    *(i32[3]) f = &arr
    *P g = &pt
    i32 h = (*g).x
    *char i = &s[0]
    *i32[2] j = [&x, &x]
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var body = program.Functions[0].Body;
        string TypeOf(int i) => model.GetExpressionType(((VariableDeclarationNode)body[i]).Initializer!);
        Assert.That(TypeOf(4), Is.EqualTo("*i32"));
        Assert.That(TypeOf(5), Is.EqualTo("**i32"));
        Assert.That(TypeOf(6), Is.EqualTo("i32"));
        Assert.That(TypeOf(9), Is.EqualTo("*(i32[3])"));
        Assert.That(TypeOf(10), Is.EqualTo("*P"));
        Assert.That(TypeOf(11), Is.EqualTo("i32"));
        Assert.That(TypeOf(12), Is.EqualTo("*char"));
    }

    [Test]
    public void Check_Pointers_AssignmentThroughDereference()
    {
        var (_, _, valid) = Check(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    **i32 pp = &p
    *p = 2
    **pp = 3
    *pp = &x
    (*p)++
    *p = *p + 1
}");
        var (_, _, mismatch) = Check("func main() -> void { i32 x = 1\n *i32 p = &x\n *p = true }");
        var (_, _, notPointer) = Check("func main() -> void { i32 x = 1\n *x = 2 }");

        Assert.That(valid.HasErrors, Is.False);
        Assert.That(CountCode(mismatch, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(notPointer, DiagnosticCodes.InvalidDereference), Is.EqualTo(1));
    }

    [Test]
    public void Check_AddressOf_RejectsNonLvalues_WithFileAwareDiagnostic()
    {
        var sources = new[]
        {
            "func main() -> void { *i32 p = &5 }",
            "func f() -> i32 { return 1 }\nfunc main() -> void { *i32 p = &f() }",
            "func main() -> void { i32 x = 1\n *i32 p = &(x + 1) }",
            "func main() -> void { i32 x = 1\n *i32 p = &x++ }",
            "func main() -> void { *char p = &'a' }",
            "func main() -> void { *string p = &\"a\" }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.InvalidAddressOperand), Is.EqualTo(1), source);
            var diagnostic = diagnostics.Diagnostics.First(d => d.Code == DiagnosticCodes.InvalidAddressOperand);
            Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
            Assert.That(diagnostic.Span, Is.Not.Null);
        }
    }

    [Test]
    public void Check_Dereference_RejectsNonPointers()
    {
        var sources = new[]
        {
            "func main() -> void { i32 x = 1 i32 y = *x }",
            "func main() -> void { i32 y = *5 }",
            "func main() -> void { string s = \"a\" char c = *s }",
            "func main() -> void { *i32[2] a = [&b, &b] i32 y = *a }",
            "func main() -> void { i32 x = 1\n *i32 p = &x i32 y = **p }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.InvalidDereference), Is.EqualTo(1), source);
            Assert.That(diagnostics.Diagnostics.First(d => d.Code == DiagnosticCodes.InvalidDereference).FileName,
                Is.EqualTo("test.lumi"));
        }
    }

    [Test]
    public void Check_Pointers_TypeCompatibilityIsByPointee()
    {
        var (_, _, ok) = Check("func main() -> void { i32 x = 1\n *i32 p = &x\n *i32 q = p }");
        var (_, _, wrongPointee) = Check("func main() -> void { i32 x = 1\n *i64 p = &x }");
        var (_, _, depth) = Check("func main() -> void { i32 x = 1\n *i32 p = &x\n **i32 q = p }");
        var (_, _, fromInt) = Check("func main() -> void { *i32 p = 5 }");
        var (_, _, toInt) = Check("func main() -> void { i32 x = 1 i32 n = &x }");
        var (_, _, args) = Check(@"
func take(*i32 p) -> void { }
func main() -> void { i64 x = 1 take(&x) }");
        var (_, _, ret) = Check("func f(i64 x) -> *i32 { return &x }");

        Assert.That(ok.HasErrors, Is.False);
        foreach (var diagnostics in new[] { wrongPointee, depth, fromInt, toInt, args, ret })
        {
            Assert.That(CountCode(diagnostics, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        }
    }

    [Test]
    public void Check_Pointers_UnknownPointeeAndMemberAccessWithoutDereference()
    {
        var (_, _, unknown) = Check("func main() -> void { *Missing p }");
        var (_, _, member) = Check(@"
struct P { i32 x }
func main() -> void { P pt = P { x: 1 } *P p = &pt i32 y = p.x }");

        Assert.That(CountCode(unknown, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(CountCode(member, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
    }

    [Test]
    public void Check_Pointers_OperatorRules()
    {
        var (_, _, equality) = Check(@"
func main() -> void {
    i32 a = 1
    *i32 p = &a
    *i32 q = &a
    bool x = p == q
    bool y = p != &a
}");
        var (_, _, arithmetic) = Check("func main() -> void { i32 a = 1\n *i32 p = &a\n *i32 q = p * 2 }");
        var (_, _, ordering) = Check("func main() -> void { i32 a = 1\n *i32 p = &a bool b = p < p }");
        var (_, _, mixed) = Check("func main() -> void { i32 a = 1\n *i32 p = &a bool b = p == a }");

        Assert.That(equality.HasErrors, Is.False);
        Assert.That(arithmetic.HasErrors, Is.True);
        Assert.That(ordering.HasErrors, Is.True);
        Assert.That(CountCode(mixed, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Pointers_InStructFieldsIncludingSelfReference()
    {
        var (_, _, valid) = Check(@"
struct Node { i32 value *Node next }
func main() -> void {
    Node a = Node { value: 1, next: &b }
}");
        var (_, _, ok) = Check(@"
struct Node { i32 value *Node next }
func main() -> void {
    Node a
    Node b
    a.next = &b
    i32 v = (*a.next).value
}");

        Assert.That(valid.Diagnostics.Select(d => d.Code), Does.Contain(DiagnosticCodes.UndefinedVariable));
        Assert.That(ok.HasErrors, Is.False);
    }

    [Test]
    public void Check_Print_AcceptsPointersOfAnyPointeeType()
    {
        var (program, model, diagnostics) = Check(@"
struct P { i32 x }
func main() -> void {
    u8 b = 1
    i32 n = 2
    P pt = P { x: 3 }
    *u8 pb = &b
    *i32 pn = &n
    *P pp = &pt
    **i32 ppn = &pn
    print(pb)
    println(pn)
    print(pp)
    println(ppn)
    println(&n)
    print(&pt.x)
    println(*pn)
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);

        // The recorded expression type is what lets codegen print the address, not the pointee.
        var printed = program.Functions[0].Body.OfType<PrintStatementNode>().ToList();
        Assert.That(printed.Select(p => model.GetExpressionType(p.Expression)),
            Is.EqualTo(new[] { "*u8", "*i32", "*P", "**i32", "*i32", "*i32", "i32" }));
    }

    [Test]
    public void Check_Print_StillReportsErrorsInsidePointerExpressions()
    {
        var (_, _, nonPointer) = Check("func main() -> void { i32 x = 1\n println(*x) }");
        var (_, _, nonLvalue) = Check("func main() -> void { println(&5) }");

        Assert.That(CountCode(nonPointer, DiagnosticCodes.InvalidDereference), Is.EqualTo(1));
        Assert.That(CountCode(nonLvalue, DiagnosticCodes.InvalidAddressOperand), Is.EqualTo(1));
    }

    [Test]
    public void Check_Usize_DeclarationsParametersReturnsFieldsAndArrays()
    {
        var (_, _, diagnostics) = Check(@"
struct Slice { usize start usize[2] bounds }
func twice(usize n) -> usize { return n + n }
func main() -> void {
    usize n = 5
    usize m = twice(n)
    usize[3] sizes = [1, 2, 3]
    Slice s = Slice { start: 1, bounds: [2, 3] }
    usize i = sizes[n - 4]
    *usize p = &n
    *p = m
    print(s.start)
    println(i)
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
    }

    [Test]
    public void Check_Usize_OperatorsKeepTheType()
    {
        var (program, model, diagnostics) = Check(@"
func main() -> void {
    usize a = 6
    usize b = 3
    usize sum = a + b
    usize diff = a - b
    usize prod = a * b
    usize quot = a / b
    usize both = a & b
    usize either = a | b
    usize xor = a ^ b
    usize inverted = ~a
    usize left = a << 2
    usize right = a >> 1
    bool eq = a == b
    bool ne = a != b
    bool lt = a < b
    bool le = a <= b
    bool gt = a > b
    bool ge = a >= b
    a++
    --b
    usize old = a++
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var body = program.Functions[0].Body;
        string TypeOf(int i) => model.GetExpressionType(((VariableDeclarationNode)body[i]).Initializer!);
        Assert.That(new[] { TypeOf(2), TypeOf(3), TypeOf(4), TypeOf(5), TypeOf(6), TypeOf(7), TypeOf(8), TypeOf(9) },
            Is.All.EqualTo("usize"));
        Assert.That(new[] { TypeOf(12), TypeOf(13), TypeOf(14), TypeOf(15), TypeOf(16), TypeOf(17) },
            Is.All.EqualTo("bool"));
    }

    [Test]
    public void Check_Usize_LiteralRanges_FollowUnsigned64Bits()
    {
        var (_, _, maximum) = Check("func main() -> void { usize n = 18446744073709551615 }");
        var (_, _, negative) = Check("func main() -> void { usize n = -1 }");

        Assert.That(maximum.HasErrors, Is.False);
        Assert.That(CountCode(negative, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
    }

    [Test]
    public void Check_Usize_WidensNarrowerUnsignedOperands()
    {
        var (program, model, diagnostics) = Check(@"
func main() -> void {
    usize n = 1000
    u8 a = 1
    u16 b = 2
    u32 c = 3
    usize r1 = n + a
    usize r2 = b * n
    usize r3 = n - c
    bool lt = a < n
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var decl = (VariableDeclarationNode)program.Functions[0].Body[4];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("usize"));
    }

    [Test]
    public void Check_Usize_DoesNotMixWithU64OrSignedIntegers()
    {
        foreach (var other in new[] { "u64", "i8", "i32", "i64" })
        {
            var (_, _, arithmetic) = Check(
                $"func main() -> void {{ usize n = 1 {other} m = 1 usize r = n + m }}");
            var (_, _, comparison) = Check(
                $"func main() -> void {{ usize n = 1 {other} m = 1 bool r = n < m }}");

            Assert.That(arithmetic.HasErrors, Is.True, $"usize + {other}");
            Assert.That(comparison.HasErrors, Is.True, $"usize < {other}");
        }
    }

    [Test]
    public void Check_Usize_IsDistinctFromOtherIntegerTypesForAssignment()
    {
        foreach (var other in new[] { "u8", "u32", "u64", "i32", "i64" })
        {
            var (_, _, toUsize) = Check($"func main() -> void {{ {other} v = 1 usize n = v }}");
            var (_, _, fromUsize) = Check($"func main() -> void {{ usize n = 1 {other} v = n }}");

            Assert.That(CountCode(toUsize, DiagnosticCodes.TypeMismatch), Is.EqualTo(1), $"{other} -> usize");
            Assert.That(CountCode(fromUsize, DiagnosticCodes.TypeMismatch), Is.EqualTo(1), $"usize -> {other}");
        }
    }

    [Test]
    public void Check_Usize_ArgumentsAndReturnsMustMatchExactly()
    {
        var (_, _, argument) = Check(@"
func take(usize n) -> void { }
func main() -> void { u64 v = 1 take(v) }");
        var (_, _, returned) = Check("func f(u64 v) -> usize { return v }");

        Assert.That(CountCode(argument, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(CountCode(returned, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Usize_RejectsSignedOnlyAndNonIntegerOperators()
    {
        var (_, _, negate) = Check("func main() -> void { usize n = 1 usize m = -n }");
        var (_, _, logical) = Check("func main() -> void { usize n = 1 bool b = !n }");
        var (_, _, withFloat) = Check("func main() -> void { usize n = 1 f32 f = 1.5 f32 g = n + f }");

        Assert.That(negate.HasErrors, Is.True);
        Assert.That(logical.HasErrors, Is.True);
        Assert.That(withFloat.HasErrors, Is.True);
    }

    [Test]
    public void Check_Usize_ErrorsAreFileAwareWithSpans()
    {
        var (_, _, diagnostics) = Check("func main() -> void { usize n = true }");

        var diagnostic = diagnostics.Diagnostics.Single(d => d.Code == DiagnosticCodes.TypeMismatch);
        Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
        Assert.That(diagnostic.Message, Does.Contain("usize"));
        Assert.That(diagnostic.Span!.Start.Column, Is.EqualTo(33));
    }

    [Test]
    public void Check_Nil_IsAssignableWhereverAPointerIsExpected()
    {
        var (program, model, diagnostics) = Check(@"
struct Node { i32 value *Node next }
func take(*i32 p) -> void { }
func none() -> *i32 { return nil }
func main() -> void {
    i32 x = 1
    *i32 p = nil
    **i32 pp = &p
    *(i32[2]) pa = nil
    p = nil
    p = &x
    *pp = nil
    take(nil)
    Node n = Node { value: 1, next: nil }
    n.next = nil
    *i32[2] arr = [nil, p]
    arr[0] = nil
    *i32 q = none()
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var decl = (VariableDeclarationNode)program.Functions[2].Body[1];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("nil"));
    }

    [Test]
    public void Check_Nil_ComparesForEqualityWithPointersAndNil()
    {
        var (program, model, diagnostics) = Check(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    **i32 pp = &p
    bool a = p == nil
    bool b = nil == p
    bool c = p != nil
    bool d = nil != p
    bool e = nil == nil
    bool f = nil != nil
    bool g = pp == nil
    if p != nil { }
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var decl = (VariableDeclarationNode)program.Functions[0].Body[3];
        Assert.That(model.GetExpressionType(decl.Initializer!), Is.EqualTo("bool"));
    }

    [Test]
    public void Check_Nil_IsRejectedForNonPointerTypes()
    {
        var sources = new[]
        {
            "func main() -> void { i8 x = nil }",
            "func main() -> void { i32 x = nil }",
            "func main() -> void { usize x = nil }",
            "func main() -> void { u64 x = nil }",
            "func main() -> void { bool x = nil }",
            "func main() -> void { char x = nil }",
            "func main() -> void { string x = nil }",
            "func main() -> void { f32 x = nil }",
            "struct P { i32 a }\nfunc main() -> void { P x = nil }",
            "func main() -> void { i32[2] x = nil }",
            "func main() -> void { i32 x = 1\n x = nil }",
            "func take(i32 n) -> void { }\nfunc main() -> void { take(nil) }",
            "func f() -> i32 { return nil }",
            "struct P { i32 a }\nfunc main() -> void { P x = P { a: nil } }",
            "func main() -> void { i32[2] x = [1, nil] }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0), source);
            Assert.That(diagnostics.Diagnostics.All(d => d.FileName == "test.lumi"), Is.True, source);
        }
    }

    [Test]
    public void Check_Nil_IsRejectedWithUnsupportedOperators()
    {
        var sources = new[]
        {
            "func main() -> void { i32 x = 1 bool b = x == nil }",
            "func main() -> void { bool b = nil == 0 }",
            "func main() -> void { string s = \"a\" bool b = s != nil }",
            "func main() -> void { *i32 p = nil bool b = nil < p }",
            "func main() -> void { *i32 p = nil bool b = p <= nil }",
            "func main() -> void { bool b = nil > nil }",
            "func main() -> void { i32 n = nil + 1 }",
            "func main() -> void { *i32 p = nil + 1 }",
            "func main() -> void { *i32 p = nil bool b = p + nil }",
            "func main() -> void { i32 n = -nil }",
            "func main() -> void { bool b = !nil }",
            "func main() -> void { bool b = nil && true }",
            "func main() -> void { i32 n = nil & 1 }",
            "func main() -> void { println(nil) }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(diagnostics.HasErrors, Is.True, source);
        }
    }

    [Test]
    public void Check_Nil_CannotBeDereferencedAddressedOrUpdated()
    {
        var (_, _, dereference) = Check("func main() -> void { i32 x = *nil }");
        var (_, _, addressOf) = Check("func main() -> void { *i32 p = &nil }");
        var (_, _, update) = Check("func main() -> void { nil++ }");

        Assert.That(CountCode(dereference, DiagnosticCodes.InvalidDereference), Is.EqualTo(1));
        Assert.That(CountCode(addressOf, DiagnosticCodes.InvalidAddressOperand), Is.EqualTo(1));
        Assert.That(CountCode(update, DiagnosticCodes.InvalidUpdateTarget), Is.EqualTo(1));
    }

    [Test]
    public void Check_Nil_DiagnosticsAreFileAwareWithSpans()
    {
        var (_, _, diagnostics) = Check("func main() -> void { i32 x = nil }");

        var diagnostic = diagnostics.Diagnostics.Single();
        Assert.That(diagnostic.FileName, Is.EqualTo("test.lumi"));
        Assert.That(diagnostic.Message, Does.Contain("nil"));
        Assert.That(diagnostic.Span!.Start.Column, Is.EqualTo(31));
    }

    [Test]
    public void Check_PointerOffsets_KeepThePointerType()
    {
        var (program, model, diagnostics) = Check(@"
func main() -> void {
    i32[4] arr = [1, 2, 3, 4]
    usize n = 2
    i32 s = -1
    *i32 p = &arr[0]
    *i32 a = p + 1
    *i32 b = 1 + p
    *i32 c = p - 1
    *i32 d = p + n
    *i32 e = n + p
    *i32 f = p - s
    *i32 g = p + s
    **i32 pp = &p
    **i32 h = pp + 1
    *u8 bytes = nil
    *u8 i = bytes + 3
}");

        Assert.That(diagnostics.Diagnostics.Select(DiagnosticFormatter.Format), Is.Empty);
        var body = program.Functions[0].Body;
        Assert.That(model.GetExpressionType(((VariableDeclarationNode)body[4]).Initializer!), Is.EqualTo("*i32"));
        Assert.That(model.GetExpressionType(((VariableDeclarationNode)body[11]).Initializer!), Is.EqualTo("**i32"));
    }

    [Test]
    public void Check_PointerOffsets_RejectInvalidOperands()
    {
        var sources = new[]
        {
            "func main() -> void { *i32 p = nil\n *i32 q = p + p }",
            "func main() -> void { *i32 p = nil\n *i32 q = 1 - p }",
            "func main() -> void { *i32 p = nil\n *i32 q = p * 2 }",
            "func main() -> void { *i32 p = nil\n *i32 q = p / 2 }",
            "func main() -> void { *i32 p = nil\n *i32 q = p + 1.5 }",
            "func main() -> void { *i32 p = nil\n *i32 q = p + true }",
            "func main() -> void { *i32 p = nil\n *i32 q = p + 'a' }",
            "func main() -> void { *i32 p = nil\n i32 q = p + 1 }",
            "func main() -> void { *i32 p = nil\n *i64 q = p + 1 }"
        };

        foreach (var source in sources)
        {
            var (_, _, diagnostics) = Check(source);

            Assert.That(CountCode(diagnostics, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0), source);
        }
    }
}
