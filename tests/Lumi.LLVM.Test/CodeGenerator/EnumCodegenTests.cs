using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class EnumCodegenTests
{
    private const string Shapes = @"
enum Message {
    Quit,
    Key(i32),
    Pair(i32, usize),
    Move { x: i32, y: i32 },
}
";

    [Fact]
    public void Enum_UnitTupleAndStructVariants_RunAndBind()
    {
        var output = Run(CompileToIr(Shapes + @"
func describe(Message m) -> void {
    switch m {
        Message.Quit => println(0)
        Message.Key(k) => println(k)
        Message.Pair(a, b) => { println(a) println(b) }
        Message.Move { x, y } => println(x * 100 + y)
    }
}
func main() -> void {
    describe(Message.Quit)
    describe(Message.Key(7))
    describe(Message.Pair(-3, 9))
    describe(Message.Move { x: 4, y: 5 })
}"));

        if (output is null) return;
        Assert.Equal("0\n7\n-3\n9\n405\n", output);
    }

    [Fact]
    public void Enum_LayoutIsTagPlusMaxPayload_SizeofAndAlignof()
    {
        var output = Run(CompileToIr(@"
enum Wide { Small(u8), Big(i64, i32), None }
enum Tiny { A, B }
struct Holder { Wide w i32 n }
func main() -> void {
    println(sizeof(Wide))
    println(alignof(Wide))
    println(sizeof(Tiny))
    println(sizeof(Holder))
}"));

        if (output is null) return;
        // Wide: tag(4) padded to 8, payload {i64,i32} = 16 -> 24; Holder: 24 + 4 padded to 32.
        Assert.Equal("24\n8\n4\n32\n", output);
    }

    [Fact]
    public void Enum_InVariablesStructFieldsReturnsAndDefaultArm()
    {
        var output = Run(CompileToIr(@"
enum Shape { Circle(i32), Square(i32), Dot }
struct Item { Shape shape i32 id }
func make(i32 n) -> Shape {
    if n > 0 { return Shape.Circle(n) }
    return Shape.Dot
}
func area(Shape s) -> i32 {
    switch s {
        Shape.Circle(r) => return r * 3
        default => return -1
    }
}
func main() -> void {
    Shape a = make(5)
    Item item = Item { shape: Shape.Square(4), id: 9 }
    println(area(a))
    println(area(make(0)))
    println(area(item.shape))
    println(item.id)
    switch item.shape {
        Shape.Square(side) => println(side)
        Shape.Circle(_) => println(0)
        Shape.Dot => println(1)
    }
}"));

        if (output is null) return;
        Assert.Equal("15\n-1\n-1\n9\n4\n", output);
    }

    [Fact]
    public void Enum_PayloadWithStructAndNestedEnum_AndModuleVerifies()
    {
        var ir = CompileToIr(@"
struct P { i32 x i32 y }
enum Inner { A(i32), B }
enum Outer { Pt(P), In(Inner), Nothing }
func main() -> void {
    Outer o = Outer.Pt(P { x: 1, y: 2 })
    Outer q = Outer.In(Inner.A(5))
    switch o {
        Outer.Pt(p) => println(p.x + p.y)
        Outer.In(i) => println(0)
        Outer.Nothing => println(1)
    }
    switch q {
        Outer.In(i) => switch i { Inner.A(v) => println(v) Inner.B => println(0) }
        default => println(9)
    }
}");

        Assert.Contains("switch i32", ir);
        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("3\n5\n", output);
    }

    [Fact]
    public void Enum_AcrossModules_PubEnumWorks()
    {
        var output = Run(CompileToIr(@"
import shapes
func main() -> void {
    shapes::Kind k = shapes::Kind.Num(41)
    switch k {
        shapes::Kind.Num(n) => println(n + 1)
        shapes::Kind.Nothing => println(0)
    }
}",
            ("shapes.lumi", @"
module shapes
pub enum Kind { Num(i32), Nothing }")));

        if (output is null) return;
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Enum_StructPatternShorthand_BindsFieldsByName()
    {
        var output = Run(CompileToIr(Shapes + @"
func main() -> void {
    Message a = Message.Move { x: 4, y: 5 }
    switch a {
        Message.Move { x, y } => println(x * 10 + y)
        default => println(0)
    }
    switch a {
        Message.Move { y, x } => println(y)
        default => println(0)
    }
}"));

        if (output is null) return;
        Assert.Equal("45\n5\n", output);
    }
}
