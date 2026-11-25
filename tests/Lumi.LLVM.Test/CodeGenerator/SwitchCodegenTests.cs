using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class SwitchCodegenTests
{
    [Fact]
    public void Switch_LowersToSwitchInstruction_WithNamedBlocks()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 x = 2
    switch x {
        1 => println(10)
        2, 3 => println(20)
        default => println(0)
    }
}");

        Assert.Contains("switch i32", ir);
        Assert.Contains("switch.arm.0:", ir);
        Assert.Contains("switch.arm.1:", ir);
        Assert.Contains("switch.default:", ir);
        Assert.Contains("switch.end:", ir);
    }

    [Fact]
    public void Switch_PicksArm_NoFallthrough_AndDefault()
    {
        var output = Run(CompileToIr(@"
func classify(i32 n) -> void {
    switch n {
        1 => println(10)
        2, 3 => { println(20) println(21) }
        -1 => println(99)
        default => println(0)
    }
    println(100)
}
func main() -> void {
    classify(1)
    classify(3)
    classify(-1)
    classify(7)
}"));

        if (output is null) return;
        Assert.Equal("10\n100\n20\n21\n100\n99\n100\n0\n100\n", output);
    }

    [Fact]
    public void Switch_WithoutDefault_SkipsWhenNothingMatches_AndEvaluatesSubjectOnce()
    {
        var output = Run(CompileToIr(@"
func next(*i32 counter) -> i32 {
    *counter = *counter + 1
    return *counter
}
func main() -> void {
    i32 c = 0
    switch next(&c) {
        5 => println(5)
        6 => println(6)
    }
    println(c)
}"));

        if (output is null) return;
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Switch_CharBoolAndSmallIntegers_Work()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    char c = 'b'
    switch c { 'a' => println(1) 'b' => println(2) }
    bool f = false
    switch f { true => println(3) false => println(4) }
    u8 b = 200
    switch b { 200 => println(5) default => println(6) }
}"));

        if (output is null) return;
        Assert.Equal("2\n4\n5\n", output);
    }

    [Fact]
    public void Switch_ArmScopes_ReturnsAndBreakWork()
    {
        var output = Run(CompileToIr(@"
func pick(i32 n) -> i32 {
    switch n {
        1 => { i32 a = 5 return a }
        2 => { i32 a = 6 return a }
        default => return 0
    }
}
func main() -> void {
    println(pick(1))
    println(pick(2))
    println(pick(9))
    i32 i = 0
    while i < 4 {
        i++
        switch i {
            2 => continue
            3 => break
            default => println(i)
        }
        println(100 + i)
    }
}"));

        if (output is null) return;
        Assert.Equal("5\n6\n0\n1\n101\n103\n4\n104\n", output);
    }
}
