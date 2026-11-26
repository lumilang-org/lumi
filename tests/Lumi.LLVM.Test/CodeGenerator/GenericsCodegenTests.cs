using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class GenericsCodegenTests
{
    private const string Vec2 = "struct Vec2 { pub i32 x pub i32 y }\n";

    [Fact]
    public void GenericFunction_ReturnsItsTypeArgument()
    {
        var output = Run(CompileToIr(@"
func test<T>() -> T {
    T value
    return value
}
func twice<T>(T v) -> T { return v }
func main() {
    println(twice<i32>(21) * 2)
    println(twice<u8>(7))
}"));

        if (output is null) return;
        Assert.Equal("42\n7\n", output);
    }

    [Fact]
    public void GenericStruct_WithMethods_AndNesting()
    {
        var ir = CompileToIr(@"
struct Box<T> {
    pub T value
    pub func get(*Box<T> self) -> T { return self.value }
}
func main() {
    Box<i32> a = Box<i32> { value: 5 }
    Box<Box<i32>> b = Box<Box<i32>> { value: a }
    println(a.get() + b.value.get())
    let inner = b.get()
    println(inner.value)
}");

        Assert.Contains("%Box__i32 = type", ir);
        Assert.Contains("%Box__Box__i32 = type", ir);
        Assert.DoesNotContain("%Box = type", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("10\n5\n", output);
    }

    [Fact]
    public void Specializations_AreShared_AndTemplatesAreNotEmitted()
    {
        var ir = CompileToIr(@"
func id<T>(T v) -> T { return v }
func main() {
    println(id<i32>(1))
    println(id<i32>(2))
    println(id<i64>(3))
}");

        Assert.Equal(1, CountOccurrences(ir, "define internal i32 @id__i32("));
        Assert.Equal(1, CountOccurrences(ir, "define internal i64 @id__i64("));
        Assert.DoesNotContain("@id(", ir);
    }

    [Fact]
    public void Where_Constraint_Satisfied_RunsWithMangledNames()
    {
        var source = Vec2 + @"
struct Mover<T> where T : Vec2 {
    pub T pos
}
func step<T>(T v) -> T where T : Vec2 {
    return T { x: v.x + 1, y: v.y + 1 }
}
func main() {
    let m = Mover<Vec2> { pos: step<Vec2>(Vec2 { x: 1, y: 2 }) }
    println(m.pos.x + m.pos.y)
}";
        var ir = CompileToIr(source);

        Assert.Contains("%Mover__Vec2 = type", ir);
        Assert.Contains("@step__Vec2(", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void Where_Constraint_Violated_FailsCompilation()
    {
        var ex = Assert.Throws<Exception>(() => CompileToIr(Vec2 + @"
func step<T>(T v) -> T where T : Vec2 { return v }
func main() { let r = step<i32>(3) }"));

        Assert.Contains("LUMI1022", ex.Message);
    }

    [Fact]
    public void Generics_WithDefer_AndArena()
    {
        var output = Run(CompileToIr(@"
import std::mem

struct Slot<T> { pub T value }

func fill<T>(T v) -> T {
    defer println(2)
    Slot<T> slot = Slot<T> { value: v }
    return slot.value
}

func main() {
    Arena arena
    arena.init(64)
    defer arena.destroy()
    println(fill<i32>(9))
}"));

        if (output is null) return;
        Assert.Equal("2\n9\n", output);
    }

    [Fact]
    public void Generic_SwitchOverEnum_IsCheckedPerSpecialization()
    {
        var output = Run(CompileToIr(@"
enum Kind { A B }
func pick<T>(Kind k, T a, T b) -> T {
    switch k {
        Kind.A => { return a }
        Kind.B => { return b }
    }
}
func main() {
    println(pick<i32>(Kind.B, 1, 2))
}"));

        if (output is null) return;
        Assert.Equal("2\n", output);
    }
}
