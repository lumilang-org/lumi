using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class EnumTests
{
    private const string Msg = @"
enum Msg {
    Quit,
    Key(i32),
    Pair(i32, usize),
    Move { x: i32, y: i32 },
}
";

    private static ProgramNode Parse(string source, string file, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(source, diagnostics, file);
        var tokens = new List<Token>();
        Token token;
        do { token = lexer.NextToken(); tokens.Add(token); } while (token.Type != TokenType.EndOfFile);
        return new Parser(tokens, diagnostics, file).ParseProgram();
    }

    private static DiagnosticBag Check(string source)
    {
        var diagnostics = new DiagnosticBag();
        var program = Parse(source, "t.lumi", diagnostics);
        new TypeChecker(diagnostics).CheckProgram(program);
        return diagnostics;
    }

    private static DiagnosticBag CheckBody(string body) => Check(Msg + "func main() -> void {\n" + body + "\n}");

    private static string Messages(DiagnosticBag d) => string.Join("\n", d.Diagnostics.Select(x => x.Message));
    private static int Count(DiagnosticBag d, string code) => d.Diagnostics.Count(x => x.Code == code);

    [Test]
    public void Parse_Enum_AllVariantKinds_AndVisibility()
    {
        var diagnostics = new DiagnosticBag();
        var program = Parse("pub " + Msg.TrimStart() + "enum Tiny { A, B }", "t.lumi", diagnostics);

        Assert.That(diagnostics.HasErrors, Is.False, Messages(diagnostics));
        Assert.That(program.Enums, Has.Count.EqualTo(2));

        var msg = program.Enums[0];
        Assert.That(msg.VisibilityModifier, Is.EqualTo(VisibilityModifier.Public));
        Assert.That(program.Enums[1].VisibilityModifier, Is.EqualTo(VisibilityModifier.Private));
        Assert.That(msg.Variants.Select(v => v.Kind), Is.EqualTo(new[]
            { EnumVariantKind.Unit, EnumVariantKind.Tuple, EnumVariantKind.Tuple, EnumVariantKind.Struct }));
        Assert.That(msg.Variants[2].Fields.Select(f => f.Type), Is.EqualTo(new[] { "i32", "usize" }));
        Assert.That(msg.Variants[3].Fields.Select(f => f.Name), Is.EqualTo(new[] { "x", "y" }));
    }

    [Test]
    public void Check_Construction_OfEveryVariantKind()
    {
        var d = CheckBody(@"
Msg a = Msg.Quit
Msg b = Msg.Key(1)
Msg c = Msg.Pair(2, 3)
Msg e = Msg.Move { x: 1, y: 2 }");
        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_Construction_Diagnostics()
    {
        Assert.That(Count(CheckBody("Msg a = Msg.Nope"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody("Msg a = Msg.Key"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody("Msg a = Msg.Key(1, 2)"), DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Pair(1)"), DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Key(true)"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody("Msg a = Msg.Move { x: 1 }"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Move { x: 1, y: 2, z: 3 }"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Move { x: 1, x: 2, y: 3 }"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Move { x: true, y: 2 }"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg a = Msg.Quit(1)"), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody("i32 a = Msg.Quit"), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Declaration_Diagnostics()
    {
        var dupVariant = Check("enum E { A, B, A }");
        var dupField = Check("enum E { M { x: i32, x: i32 } }");
        var badType = Check("enum E { A(Missing) }");
        var dupEnum = Check("enum E { A }\nenum E { B }");
        var clash = Check("struct E { i32 a }\nenum E { B }");

        Assert.That(Count(dupVariant, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(dupField, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(badType, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(dupEnum, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(clash, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Switch_BindsPayloadsWithTheirTypes()
    {
        var d = CheckBody(@"
Msg m = Msg.Key(1)
switch m {
    Msg.Quit => println(0)
    Msg.Key(k) => { i32 doubled = k * 2 println(doubled) }
    Msg.Pair(a, b) => { i32 x = a usize y = b println(x) println(y) }
    Msg.Move { x, y } => println(x)
}");
        Assert.That(d.HasErrors, Is.False, Messages(d));
    }

    [Test]
    public void Check_Switch_BindingTypeIsEnforced_AndScopedToTheArm()
    {
        var wrongType = CheckBody("Msg m = Msg.Key(1)\nswitch m { Msg.Key(k) => { bool b = k } default => println(0) }");
        var leaked = CheckBody("Msg m = Msg.Key(1)\nswitch m { Msg.Key(k) => println(k) default => println(k) }");

        Assert.That(Count(wrongType, DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(leaked, DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_Switch_PatternDiagnostics()
    {
        string Arm(string pattern) => $"Msg m = Msg.Quit\nswitch m {{ {pattern} => println(0) default => println(1) }}";

        Assert.That(Count(CheckBody(Arm("Msg.Nope")), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody(Arm("Msg.Key")), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody(Arm("Msg.Quit(x)")), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody(Arm("Msg.Pair(a)")), DiagnosticCodes.ArgumentCountMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("Msg.Key(1)")), DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("Msg.Move { x }")), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("Msg.Move { z, x, y }")), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("5")), DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg m = Msg.Quit\nswitch m { Msg.Key(a), Msg.Pair(b, c) => println(0) default => println(1) }"),
            DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Switch_DuplicateVariants_AndExhaustiveness()
    {
        var dup = CheckBody("Msg m = Msg.Quit\nswitch m { Msg.Quit => println(0) Msg.Quit => println(1) default => println(2) }");
        var missing = CheckBody("Msg m = Msg.Quit\nswitch m { Msg.Quit => println(0) Msg.Key(k) => println(k) }");
        var withDefault = CheckBody("Msg m = Msg.Quit\nswitch m { Msg.Quit => println(0) default => println(1) }");
        var full = CheckBody(@"
Msg m = Msg.Quit
switch m { Msg.Quit, Msg.Key(_) => println(0) Msg.Pair(_, _) => println(1) Msg.Move { x, y } => println(2) }");
        var otherEnum = Check(Msg + "enum Other { Z }\nfunc main() -> void { Msg m = Msg.Quit\nswitch m { Other.Z => println(0) default => println(1) } }");

        Assert.That(Count(dup, DiagnosticCodes.DuplicateCase), Is.EqualTo(1));
        Assert.That(Count(missing, DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
        Assert.That(missing.Diagnostics.Single(x => x.Code == DiagnosticCodes.InvalidSwitch).Message, Does.Contain("'Pair'").And.Contain("'Move'"));
        Assert.That(withDefault.HasErrors, Is.False, Messages(withDefault));
        Assert.That(full.HasErrors, Is.False, Messages(full));
        Assert.That(Count(otherEnum, DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
    }

    [Test]
    public void Check_Enum_Visibility_AcrossModules()
    {
        const string module = @"
module shapes
pub enum Open { A(i32), B }
enum Closed { C }
";
        DiagnosticBag Run(string main)
        {
            var d = new DiagnosticBag();
            var modules = new List<ProgramNode> { Parse(module, "shapes.lumi", d), Parse(main, "main.lumi", d) };
            new TypeChecker(d).CheckCompilationUnit(modules);
            return d;
        }

        var ok = Run("import shapes\nfunc main() -> void { shapes::Open o = shapes::Open.A(1)\nswitch o { shapes::Open.A(v) => println(v) shapes::Open.B => println(0) } }");
        var privateType = Run("import shapes\nfunc take(shapes::Closed c) -> void { }\nfunc main() -> void { }");
        var privateUnit = Run("import shapes\nfunc main() -> void { i32 x = 1\nshapes::Closed c = shapes::Closed.C }");

        Assert.That(ok.HasErrors, Is.False, Messages(ok));
        Assert.That(Count(privateType, DiagnosticCodes.PrivateAccess), Is.EqualTo(1));
        Assert.That(Count(privateUnit, DiagnosticCodes.PrivateAccess), Is.GreaterThan(0));
    }

    [Test]
    public void Check_ExhaustiveEnumSwitch_WhereEveryArmReturns_NeedsNoTrailingReturn()
    {
        var ok = Check("enum E { A, B(i32) }\nfunc f(E e) -> i32 { switch e { E.A => return 0 E.B(v) => return v } }");
        var partial = Check("enum E { A, B(i32) }\nfunc f(E e) -> i32 { switch e { E.A => return 0 E.B(v) => println(v) } }");

        Assert.That(ok.HasErrors, Is.False, Messages(ok));
        Assert.That(Count(partial, DiagnosticCodes.MissingReturn), Is.EqualTo(1));
    }

    [Test]
    public void Check_StructPatterns_Shorthand()
    {
        string Arm(string pattern, string body) =>
            $"Msg m = Msg.Quit\nswitch m {{ {pattern} => {{ {body} }} default => println(0) }}";

        var shorthand = CheckBody(Arm("Msg.Move { x, y }", "i32 sum = x + y println(sum)"));
        var trailing = CheckBody(Arm("Msg.Move { x, y, }", "println(x) println(y)"));
        foreach (var d in new[] { shorthand, trailing })
        {
            Assert.That(d.HasErrors, Is.False, Messages(d));
        }

        // The shorthand binds a local of the field's type, scoped to the arm.
        Assert.That(Count(CheckBody(Arm("Msg.Move { x, y }", "bool b = x")), DiagnosticCodes.TypeMismatch), Is.GreaterThan(0));
        Assert.That(Count(CheckBody(Arm("Msg.Move { x, y }", "println(z)")), DiagnosticCodes.UndefinedVariable), Is.EqualTo(1));
    }

    [Test]
    public void Check_StructPatterns_ShorthandDiagnostics()
    {
        string Arm(string pattern) => $"Msg m = Msg.Quit\nswitch m {{ {pattern} => println(0) default => println(1) }}";

        Assert.That(Count(CheckBody(Arm("Msg.Move { x }")), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("Msg.Move { x, x, y }")), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody(Arm("Msg.Move { x, z, y }")), DiagnosticCodes.TypeMismatch), Is.EqualTo(1));
        Assert.That(Count(CheckBody("Msg m = Msg.Quit\nswitch m { Msg.Quit, Msg.Move { x, y } => println(0) default => println(1) }"),
            DiagnosticCodes.InvalidSwitch), Is.EqualTo(1));
    }

    [Test]
    public void Parse_Shorthand_IsOnlyAPatternForm()
    {
        var pattern = Parse(Msg + "func f(Msg m) -> void { switch m { Msg.Move { x, y } => println(x) default => println(0) } }",
            "t.lumi", new DiagnosticBag());
        var diagnostics = new DiagnosticBag();
        Parse(Msg + "func f() -> void { Msg m = Msg.Move { x, y } }", "t.lumi", diagnostics);

        Assert.That(pattern.Functions, Has.Count.EqualTo(1));
        Assert.That(diagnostics.HasErrors, Is.True);
    }

    [Test]
    public void Parse_StructPatterns_RejectExplicitBindings()
    {
        foreach (var pattern in new[] { "Msg.Move { x: a, y: b }", "Msg.Move { x, y: _ }", "Msg.Move { x: _, y }" })
        {
            var diagnostics = new DiagnosticBag();
            Parse(Msg + $"func f(Msg m) -> void {{ switch m {{ {pattern} => println(0) default => println(1) }} }}",
                "t.lumi", diagnostics);

            Assert.That(diagnostics.HasErrors, Is.True, pattern);
            Assert.That(Messages(diagnostics), Does.Contain("bind fields by name"), pattern);
        }
    }
}
