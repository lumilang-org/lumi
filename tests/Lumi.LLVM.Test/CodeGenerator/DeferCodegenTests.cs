using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class DeferCodegenTests
{
    [Fact]
    public void Defer_RunsAtBlockExit_InLifoOrder()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    defer println(1)
    defer println(2)
    defer { println(3) println(4) }
    println(0)
}"));

        if (output is null) return;
        Assert.Equal("0\n3\n4\n2\n1\n", output);
    }

    [Fact]
    public void Defer_EarlyReturn_RunsAllEnclosingDefers_AfterEvaluatingValue()
    {
        var output = Run(CompileToIr(@"
func f(i32 n) -> i32 {
    i32 x = n
    defer println(100)
    if n > 0 {
        defer { x = 99 println(200) }
        return x
    }
    println(300)
    return 0
}
func main() -> void {
    println(f(5))
    println(f(0))
}"));

        if (output is null) return;
        // f(5): value 5 is captured before the defers run (which set x = 99).
        Assert.Equal("200\n100\n5\n300\n100\n0\n", output);
    }

    [Fact]
    public void Defer_NestedScopes_RunWhenEachScopeEnds()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    defer println(1)
    if true {
        defer println(2)
        println(10)
    }
    println(11)
}"));

        if (output is null) return;
        Assert.Equal("10\n2\n11\n1\n", output);
    }

    [Fact]
    public void Defer_InLoop_RunsEveryIteration_AndOnBreakAndContinue()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    for i32 i = 0; i < 4; i++ {
        defer println(50 + i)
        if i == 1 { continue }
        if i == 2 { break }
        println(i)
    }
    println(99)
}"));

        if (output is null) return;
        Assert.Equal("0\n50\n51\n52\n99\n", output);
    }

    [Fact]
    public void Defer_BreakInsideSwitch_OnlyUnwindsSwitch_ContinueUnwindsLoop()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    for i32 i = 0; i < 2; i++ {
        defer println(7)
        switch i {
            0 => {
                defer println(1)
                break
            }
            default => {
                defer println(2)
                continue
            }
        }
        println(8)
    }
}"));

        if (output is null) return;
        Assert.Equal("1\n8\n7\n2\n7\n", output);
    }

    [Fact]
    public void Defer_BeforeBareReturn_Verifies_AndRuns()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    defer println(1)
    return
}"));

        if (output is null) return;
        Assert.Equal("1\n", output);
    }
}
