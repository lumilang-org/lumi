using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

/// <summary>Compiles and runs std::mem's block-based Arena (nil pointers, pointer offsets, casts).</summary>
public class ArenaSmokeTests
{
    [Fact]
    public void StdMem_CompilesAndVerifies()
    {
        var ir = CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    arena.init(16)
    arena.destroy()
}");

        Assert.Contains("declare ptr @malloc(i64", ir);
        Assert.Contains("declare void @free(ptr", ir);
        Assert.Contains("ret ptr null", ir);
        Assert.Contains("getelementptr i8, ptr", ir);
    }

    [Fact]
    public void Arena_SmokeTest()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    println(arena.init(64))
    println(arena.head != nil)
    println(arena.block_capacity)

    *u8 a = arena.alloc(10, 1)
    *u8 b = arena.alloc(8, 8)
    println(a != nil)
    println(b != nil)
    println(b == a + 16)

    *a = 7
    *b = 9
    println(*a)
    println(*b)
    println(*(a + 16))

    println(arena.alloc(8, 0) == nil)
    println(arena.alloc(8, 3) == nil)

    arena.reset()
    println(arena.alloc(4, 4) == a)

    arena.destroy()
    println(arena.head == nil)
    println(arena.current == nil)
    println(arena.block_capacity)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal(
            "true\ntrue\n64\ntrue\ntrue\ntrue\n7\n9\n9\ntrue\ntrue\ntrue\ntrue\ntrue\n0\n",
            output.Replace("\r", ""));
    }

    [Fact]
    public void Arena_InitWithZeroCapacity_LeavesNoBlocks()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    println(arena.init(0))
    println(arena.head == nil)
    println(arena.alloc(1, 1) == nil)
    arena.destroy()
    println(arena.head == nil)
}"));

        if (output is null) return;
        Assert.Equal("false\ntrue\ntrue\ntrue\n", output.Replace("\r", ""));
    }

    [Fact]
    public void Arena_GrowsWithANewBlockWhenTheCurrentOneIsFull()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    arena.init(16)
    *u8 a = arena.alloc(8, 8)
    *u8 b = arena.alloc(8, 8)
    println(b == a + 8)
    println(arena.head == arena.current)

    *u8 c = arena.alloc(8, 8)
    println(c != nil)
    println(arena.head == arena.current)
    println(arena.head.next == arena.current)

    *u8 big = arena.alloc(100, 1)
    println(big != nil)
    println(arena.current.capacity)
    *c = 5
    *big = 6
    println(*c + *big)
    arena.destroy()
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\ntrue\nfalse\ntrue\ntrue\n108\n11\n", output.Replace("\r", ""));
    }
}
