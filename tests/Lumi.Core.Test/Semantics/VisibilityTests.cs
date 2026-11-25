using Lumi.Core.Ast.Declarations;
using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class VisibilityTests
{
    private static ProgramNode Parse(string source, string file, DiagnosticBag diagnostics)
    {
        var lexer = new Lexer(source, diagnostics, file);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.EndOfFile);

        return new Parser(tokens, diagnostics, file).ParseProgram();
    }

    // Checks `shapes` (module) together with a main file that imports it.
    private static DiagnosticBag CheckTwoModules(string module, string main)
    {
        var diagnostics = new DiagnosticBag();
        var modules = new List<ProgramNode>
        {
            Parse(module, "shapes.lumi", diagnostics),
            Parse(main, "main.lumi", diagnostics)
        };

        Assert.That(diagnostics.HasErrors, Is.False,
            string.Join("\n", diagnostics.Diagnostics.Select(d => d.Message)));

        new TypeChecker(diagnostics).CheckCompilationUnit(modules);
        return diagnostics;
    }

    private static int Private(DiagnosticBag diagnostics)
    {
        return diagnostics.Diagnostics.Count(d => d.Code == DiagnosticCodes.PrivateAccess);
    }

    private const string Shapes = @"
module shapes
pub struct Point {
    pub i32 x
    pub i32 y
}
pub struct Hidden {
    i32 secret
    pub i32 open

    pub func make(i32 v) -> Hidden { return Hidden { secret: v, open: v } }
    pub func get(*Hidden self) -> i32 { return self.secret }
    func internal_get(*Hidden self) -> i32 { return self.secret }
    func internal_make() -> Hidden { return Hidden { secret: 1, open: 1 } }
}
struct Internal { pub i32 v }
pub func open_fn() -> i32 { return 1 }
func closed_fn() -> i32 { return 2 }
";

    [Test]
    public void Parse_Pub_SetsVisibilityOnEveryDeclaration()
    {
        var diagnostics = new DiagnosticBag();
        var program = Parse(Shapes, "shapes.lumi", diagnostics);

        Assert.That(diagnostics.HasErrors, Is.False);
        Assert.That(program.Structs[0].VisibilityModifier, Is.EqualTo(VisibilityModifier.Public));
        Assert.That(program.Structs[2].VisibilityModifier, Is.EqualTo(VisibilityModifier.Private));
        Assert.That(program.Structs[1].Fields[0].VisibilityModifier, Is.EqualTo(VisibilityModifier.Private));
        Assert.That(program.Structs[1].Fields[1].VisibilityModifier, Is.EqualTo(VisibilityModifier.Public));
        Assert.That(program.Structs[1].Methods[0].VisibilityModifier, Is.EqualTo(VisibilityModifier.Public));
        Assert.That(program.Structs[1].Methods[2].VisibilityModifier, Is.EqualTo(VisibilityModifier.Private));
        Assert.That(program.Functions[0].VisibilityModifier, Is.EqualTo(VisibilityModifier.Public));
        Assert.That(program.Functions[1].VisibilityModifier, Is.EqualTo(VisibilityModifier.Private));
    }

    [Test]
    public void Parse_Pub_DoesNotChangeFieldList()
    {
        var program = Parse(Shapes, "shapes.lumi", new DiagnosticBag());

        Assert.That(program.Structs[1].Fields.Select(f => f.Name), Is.EqualTo(new[] { "secret", "open" }));
        Assert.That(program.Structs[1].Methods.Select(m => m.Name),
            Is.EqualTo(new[] { "make", "get", "internal_get", "internal_make" }));
    }

    [Test]
    public void Check_SameModule_AccessesEverything()
    {
        var diagnostics = CheckTwoModules(Shapes + @"
func inside() -> i32 {
    Hidden h = Hidden { secret: 3, open: 4 }
    Internal i = Internal { v: 1 }
    return closed_fn() + h.secret + h.internal_get() + Hidden.internal_make().secret + i.v
}", "func main() -> void { }");

        Assert.That(diagnostics.HasErrors, Is.False,
            string.Join("\n", diagnostics.Diagnostics.Select(d => d.Message)));
    }

    [Test]
    public void Check_CrossModule_PublicItemsWork()
    {
        var diagnostics = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Point p = shapes::Point { x: 1, y: 2 }
    p.x = 5
    i32 a = p.y + shapes::open_fn()
    shapes::Hidden h = shapes::Hidden.make(7)
    i32 o = h.open
    h.open = 3
    i32 g = h.get()
}");

        Assert.That(diagnostics.HasErrors, Is.False,
            string.Join("\n", diagnostics.Diagnostics.Select(d => d.Message)));
    }

    [Test]
    public void Check_CrossModule_PrivateFunction_IsRejected()
    {
        var diagnostics = CheckTwoModules(Shapes, "import shapes\nfunc main() -> void { i32 a = shapes::closed_fn() }");
        Assert.That(Private(diagnostics), Is.EqualTo(1));
    }

    [Test]
    public void Check_CrossModule_PrivateStruct_IsRejected()
    {
        var asType = CheckTwoModules(Shapes, "import shapes\nfunc take(*shapes::Internal i) -> void { }\nfunc main() -> void { }");
        var asLiteral = CheckTwoModules(Shapes,
            "import shapes\nfunc main() -> void { shapes::Internal i = shapes::Internal { v: 1 } }");

        Assert.That(Private(asType), Is.EqualTo(1));
        Assert.That(Private(asLiteral), Is.GreaterThan(0));
    }

    [Test]
    public void Check_CrossModule_PrivateField_ReadAndWriteAreRejected()
    {
        var read = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Hidden h = shapes::Hidden.make(1)
    i32 s = h.secret
}");
        var write = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Hidden h = shapes::Hidden.make(1)
    h.secret = 2
}");
        var viaPointer = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Hidden h = shapes::Hidden.make(1)
    *shapes::Hidden p = &h
    i32 s = p.secret
}");

        Assert.That(Private(read), Is.EqualTo(1));
        Assert.That(Private(write), Is.EqualTo(1));
        Assert.That(Private(viaPointer), Is.EqualTo(1));
    }

    [Test]
    public void Check_CrossModule_PrivateMethodAndAssociatedFunction_AreRejected()
    {
        var method = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Hidden h = shapes::Hidden.make(1)
    i32 s = h.internal_get()
}");
        var associated = CheckTwoModules(Shapes, @"
import shapes
func main() -> void {
    shapes::Hidden h = shapes::Hidden.internal_make()
}");

        Assert.That(Private(method), Is.EqualTo(1));
        Assert.That(Private(associated), Is.EqualTo(1));
    }

    [Test]
    public void Check_CrossModule_StructLiteral_NeedsEveryFieldPublic()
    {
        var allPublic = CheckTwoModules(Shapes,
            "import shapes\nfunc main() -> void { shapes::Point p = shapes::Point { x: 1, y: 2 } }");
        var mixed = CheckTwoModules(Shapes,
            "import shapes\nfunc main() -> void { shapes::Hidden h = shapes::Hidden { secret: 1, open: 2 } }");

        Assert.That(allPublic.HasErrors, Is.False);
        Assert.That(Private(mixed), Is.EqualTo(1));
        Assert.That(mixed.Diagnostics.Single(d => d.Code == DiagnosticCodes.PrivateAccess).Message,
            Does.Contain("'secret'"));
    }

    [Test]
    public void Check_CrossModule_LiteralOfPrivateStructWithPublicFields_IsRejected()
    {
        var diagnostics = CheckTwoModules(Shapes,
            "import shapes\nfunc main() -> void { shapes::Internal i = shapes::Internal { v: 1 } }");
        Assert.That(Private(diagnostics), Is.GreaterThan(0));
    }

    [Test]
    public void Check_RootModule_FunctionsAreVisibleToEachOther()
    {
        var diagnostics = new DiagnosticBag();
        var program = Parse("func helper() -> i32 { return 1 }\nfunc main() -> void { i32 a = helper() }",
            "main.lumi", diagnostics);
        new TypeChecker(diagnostics).CheckProgram(program);

        Assert.That(diagnostics.HasErrors, Is.False);
    }
}
