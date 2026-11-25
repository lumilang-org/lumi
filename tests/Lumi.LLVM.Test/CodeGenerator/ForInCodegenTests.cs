using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class ForInCodegenTests
{
    [Fact]
    public void ForIn_EmitsLoopBlocks_AndVerifies()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32[3] arr = [1, 2, 3]
    for x in arr {
        if x == 2 { continue }
        if x == 3 { break }
        println(x)
    }
}");

        foreach (var label in new[] { "for.cond:", "for.body:", "for.latch:", "for.end:" })
        {
            Assert.Contains(label, ir);
        }
    }

    [Fact]
    public void ForIn_ReadsElementsNestedAndWithBreakContinue()
    {
        var output = Run(CompileToIr(@"
struct Pair { i32 a i32 b }
struct Box { i64[3] vals }
func main() -> void {
    i32[4] arr = [1, 2, 3, 4]
    let sum = 0
    for x in arr { sum = sum + x }
    println(sum)

    for x in arr {
        if x == 2 { continue }
        if x == 4 { break }
        println(x)
    }

    for a in [1, 2] {
        for b in [10, 20] { println(a * b) }
    }

    Pair[2] pairs = [Pair { a: 1, b: 2 }, Pair { a: 3, b: 4 }]
    for p in pairs { println(p.a + p.b) }

    Box box = Box { vals: [5, 6, 7] }
    let total: i64 = 0
    for v in box.vals { total = total + v }
    println(total)

    for x in arr { x = 99 }
    println(arr[0])

    for s in [""a"", ""b""] { println(s) }
    for flag in [true, false] { println(flag) }
}"));

        if (output is null) return;
        Assert.Equal(
            "10\n1\n3\n10\n20\n20\n40\n3\n7\n18\n1\na\nb\ntrue\nfalse\n",
            output.Replace("\r", ""));
    }

    [Fact]
    public void ForIn_IteratesAnArrayOfPointersFromAnArena()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    std::mem::arena_init(&arena, 64)
    *i32[2] ptrs = [nil, nil]
    for p in ptrs { println(p == nil) }
    std::mem::arena_destroy(&arena)
    for i in [1, 2, 3] { if i == 2 { break } }
    println(1)
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\n1\n", output.Replace("\r", ""));
    }
}
