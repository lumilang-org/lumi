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
    std::mem::arena_init(&arena, 16)
    std::mem::arena_destroy(&arena)
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
    println(std::mem::arena_init(&arena, 64))
    println(arena.head != nil)
    println(arena.block_capacity)

    *u8 a = std::mem::arena_alloc(&arena, 10, 1)
    *u8 b = std::mem::arena_alloc(&arena, 8, 8)
    println(a != nil)
    println(b != nil)
    println(b == a + 16)

    *a = 7
    *b = 9
    println(*a)
    println(*b)
    println(*(a + 16))

    println(std::mem::arena_alloc(&arena, 8, 0) == nil)
    println(std::mem::arena_alloc(&arena, 8, 3) == nil)

    std::mem::arena_reset(&arena)
    println(std::mem::arena_alloc(&arena, 4, 4) == a)

    std::mem::arena_destroy(&arena)
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
    println(std::mem::arena_init(&arena, 0))
    println(arena.head == nil)
    println(std::mem::arena_alloc(&arena, 1, 1) == nil)
    std::mem::arena_destroy(&arena)
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
    std::mem::arena_init(&arena, 16)
    *u8 a = std::mem::arena_alloc(&arena, 8, 8)
    *u8 b = std::mem::arena_alloc(&arena, 8, 8)
    println(b == a + 8)
    println(arena.head == arena.current)

    *u8 c = std::mem::arena_alloc(&arena, 8, 8)
    println(c != nil)
    println(arena.head == arena.current)
    println(arena.head.next == arena.current)

    *u8 big = std::mem::arena_alloc(&arena, 100, 1)
    println(big != nil)
    println(arena.current.capacity)
    *c = 5
    *big = 6
    println(*c + *big)
    std::mem::arena_destroy(&arena)
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\ntrue\nfalse\ntrue\ntrue\n108\n11\n", output.Replace("\r", ""));
    }
}
