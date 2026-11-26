using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class SizeOfCodegenTests
{
    [Fact]
    public void SizeOfAndAlignOf_FoldToUsizeConstants()
    {
        var ir = CompileToIr(@"
struct Pair { u8 a u64 b }
func main() -> void {
    usize s = sizeof(Pair)
    usize a = alignof(Pair)
}");

        Assert.Contains("store i64 16, ptr %s", ir);
        Assert.Contains("store i64 8, ptr %a", ir);
    }

    [Fact]
    public void SizeOfAndAlignOf_RunWithTargetValues()
    {
        var output = Run(CompileToIr(@"
struct Pair { u8 a u64 b }
struct Small { u8 a u16 b }
func main() -> void {
    println(sizeof(u8))
    println(sizeof(u16))
    println(sizeof(i32))
    println(sizeof(u64))
    println(sizeof(bool))
    println(sizeof(*u8))
    println(sizeof(Pair))
    println(alignof(Pair))
    println(sizeof(Small))
    println(alignof(Small))
    println(sizeof(i32[5]))
    println(alignof(i32[5]))
    println(sizeof(Pair) * 2)
}"));

        if (output is null) return;
        Assert.Equal("1\n2\n4\n8\n1\n8\n16\n8\n4\n2\n20\n4\n32\n", output.Replace("\r", ""));
    }

    [Fact]
    public void ArenaAlloc_WithSizeOfAndAlignOf_RunsThroughACastPointer()
    {
        var output = Run(CompileToIr(@"
import std::mem
struct Pair { u64 a u64 b }
func main() -> void {
    std::mem::Arena arena
    arena.init(64)
    *Pair p = arena.alloc(sizeof(Pair), alignof(Pair)) as *Pair
    (*p).b = 3
    println((*p).b)
    println(arena.head.offset)
}"));

        if (output is null) return;
        Assert.Equal("3\n16\n", output.Replace("\r", ""));
    }
}
