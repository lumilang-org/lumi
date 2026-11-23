using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

/// <summary>Compiles and runs std::mem's Arena, which uses nil pointers and pointer offsets.</summary>
public class ArenaSmokeTests
{
    [Fact]
    public void StdMem_CompilesAndVerifies_WithoutUsingZeroAsAPointer()
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
        Assert.Contains("store ptr null", ir);
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
    println(arena.buf != nil)
    println(arena.capacity)

    *u8 a = std::mem::arena_alloc(&arena, 10, 1)
    *u8 b = std::mem::arena_alloc(&arena, 8, 8)
    println(a != nil)
    println(b != nil)
    println(a == arena.buf)
    println(arena.offset)
    println(b == a + 16)

    *a = 7
    *b = 9
    println(*a)
    println(*b)
    println(*(a + 16))

    println(std::mem::arena_alloc(&arena, 100, 1) == nil)
    println(std::mem::arena_alloc(&arena, 8, 0) == nil)
    println(std::mem::arena_alloc(&arena, 8, 3) == nil)
    println(arena.offset)

    std::mem::arena_reset(&arena)
    println(arena.offset)
    println(std::mem::arena_alloc(&arena, 4, 4) == a)

    std::mem::arena_destroy(&arena)
    println(arena.buf == nil)
    println(arena.offset)
    println(arena.capacity)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal(
            "true\ntrue\n64\ntrue\ntrue\ntrue\n24\ntrue\n7\n9\n9\ntrue\ntrue\ntrue\n24\n0\ntrue\ntrue\n0\n0\n",
            output);
    }

    [Fact]
    public void Arena_InitWithZeroCapacity_LeavesANilBuffer()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    println(std::mem::arena_init(&arena, 0))
    println(arena.buf == nil)
    println(arena.capacity)
    println(std::mem::arena_alloc(&arena, 1, 1) == nil)
    std::mem::arena_destroy(&arena)
    println(arena.buf == nil)
}"));

        if (output is null) return;
        Assert.Equal("false\ntrue\n0\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Arena_AllocationsExactlyFillTheCapacityThenFail()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    std::mem::arena_init(&arena, 16)
    println(std::mem::arena_alloc(&arena, 8, 8) != nil)
    println(std::mem::arena_alloc(&arena, 8, 8) != nil)
    println(std::mem::arena_alloc(&arena, 1, 1) == nil)
    println(arena.offset)
    std::mem::arena_destroy(&arena)
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\ntrue\n16\n", output);
    }
}
