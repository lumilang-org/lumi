using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class StructFunctionCodegenTests
{
    private const string Counter = @"
struct Counter {
    i32 value

    func new(i32 start) -> Counter { return Counter { value: start } }
    func bump(*Counter self, i32 by) -> void { self.value = self.value + by }
    func get(Counter self) -> i32 { return self.value }
    func twice(*Counter self) -> i32 {
        self.bump(1)
        self.bump(1)
        return self.get()
    }
}
";

    [Fact]
    public void StructFunctions_LowerToMangledTopLevelFunctions_AndVerify()
    {
        var ir = CompileToIr(Counter + @"
func main() -> void {
    Counter c = Counter.new(1)
    c.bump(2)
    println(c.get())
}");

        Assert.Contains("define internal %Counter @Counter__new(i32", ir);
        Assert.Contains("define internal void @Counter__bump(ptr", ir);
        Assert.Contains("define internal i32 @Counter__get(%Counter", ir);
        Assert.Contains("call void @Counter__bump(ptr %c", ir);
    }

    [Fact]
    public void StructFunctions_AutoReferenceAndDereferenceReceivers()
    {
        var output = Run(CompileToIr(Counter + @"
func main() -> void {
    let c = Counter.new(10)
    c.bump(5)
    let p = &c
    p.bump(2)
    println(c.get())
    println(p.get())
    println(c.twice())
    println(p.twice())
}"));

        if (output is null) return;
        Assert.Equal("17\n17\n19\n21\n", output.Replace("\r", ""));
    }

    [Fact]
    public void StructFunctions_DoNotChangeSizeOrAlignment()
    {
        var output = Run(CompileToIr(@"
struct Plain { i32 a i64 b }
struct WithFunctions {
    i32 a
    func first(*WithFunctions self) -> i32 { return self.a }
    i64 b
    func make() -> WithFunctions { return WithFunctions { a: 1, b: 2 } }
}
func main() -> void {
    println(sizeof(Plain))
    println(sizeof(WithFunctions))
    println(alignof(Plain))
    println(alignof(WithFunctions))
}"));

        if (output is null) return;
        Assert.Equal("16\n16\n8\n8\n", output.Replace("\r", ""));
    }

    [Fact]
    public void ArenaMethods_FromStdMem_RunEndToEnd()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    println(arena.init(64))
    let a = arena.alloc(8, 8)
    let b = arena.alloc(8, 8)
    println(b == a + 8)
    arena.reset()
    println(arena.alloc(4, 4) == a)
    arena.destroy()
    println(arena.head == nil)
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output.Replace("\r", ""));
    }
}
