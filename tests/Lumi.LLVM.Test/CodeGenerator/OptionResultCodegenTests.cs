using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class OptionResultCodegenTests
{
    private const string ParseError = "enum ParseError { EmptyInput BadDigit }\n";

    [Fact]
    public void Option_SomeAndNone_SwitchBothBranches()
    {
        var ir = CompileToIr(@"
func find(i32 n) -> Option<i32> {
    if n > 0 { return Option<i32>.Some(n) }
    return Option<i32>.None()
}
func show(Option<i32> o) {
    switch o {
        Some(v) => println(v)
        None => println(-1)
    }
}
func main() {
    show(find(5))
    show(find(0))
    Option<i32> a = Option<i32>.Some(10)
    switch a {
        Option.Some(v) => println(v + 1)
        Option.None() => println(0)
    }
}");

        Assert.Contains("%Option__i32 = type", ir);
        Assert.Contains("switch i32", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("5\n-1\n11\n", output);
    }

    [Fact]
    public void Option_Nested_RoundTrips()
    {
        var output = Run(CompileToIr(@"
func main() {
    Option<Option<i32>> a = Option<Option<i32>>.Some(Option<i32>.Some(7))
    Option<Option<i32>> b = Option<Option<i32>>.Some(Option<i32>.None())
    Option<Option<i32>> c = Option<Option<i32>>.None()
    let all = (a, b, c)
    println(depth(a))
    println(depth(b))
    println(depth(c))
}
func depth(Option<Option<i32>> o) -> i32 {
    switch o {
        Some(inner) => {
            switch inner {
                Some(x) => { return x }
                None => { return 0 }
            }
        }
        None => { return -1 }
    }
}"));

        if (output is null) return;
        Assert.Equal("7\n0\n-1\n", output);
    }

    [Fact]
    public void Result_WithCustomErrorEnum_NestedSwitch()
    {
        var output = Run(CompileToIr(ParseError + @"
func parse(i32 n) -> Result<i32, ParseError> {
    if n == 0 { return Result<i32, ParseError>.Err(ParseError.EmptyInput) }
    if n < 0 { return Result<i32, ParseError>.Err(ParseError.BadDigit) }
    return Result<i32, ParseError>.Ok(n * 2)
}
func main() {
    for i32 i = -1; i < 2; i++ {
        switch parse(i) {
            Ok(v) => println(v)
            Err(e) => {
                switch e {
                    ParseError.EmptyInput => println(100)
                    ParseError.BadDigit => println(200)
                }
            }
        }
    }
}"));

        if (output is null) return;
        Assert.Equal("200\n100\n2\n", output);
    }

    [Fact]
    public void Result_WithStructPayload()
    {
        var output = Run(CompileToIr(@"
struct Vec2 { pub i32 x pub i32 y }
func main() {
    Result<Vec2, u8> ok = Result<Vec2, u8>.Ok(Vec2 { x: 3, y: 4 })
    Result<Vec2, u8> bad = Result<Vec2, u8>.Err(9)
    switch ok {
        Result.Ok(p) => println(p.x + p.y)
        Result.Err(c) => println(c)
    }
    switch bad {
        Result.Ok(p) => println(p.x)
        Result.Err(c) => println(c)
    }
}"));

        if (output is null) return;
        Assert.Equal("7\n9\n", output);
    }

    [Fact]
    public void Result_WithArenaAndDefer_RunsCleanupOnOkAndErrReturns()
    {
        var output = Run(CompileToIr(@"
import std::mem

" + ParseError + @"
func work(i32 n) -> Result<i32, ParseError> {
    Arena arena
    arena.init(64)
    defer arena.destroy()
    defer println(900 + n)
    if n == 0 { return Result<i32, ParseError>.Err(ParseError.EmptyInput) }
    return Result<i32, ParseError>.Ok(n)
}

func main() {
    for i32 i = 0; i < 2; i++ {
        switch work(i) {
            Ok(v) => println(v)
            Err(e) => {
                switch e {
                    ParseError.EmptyInput => println(1)
                    ParseError.BadDigit => println(2)
                }
            }
        }
    }
}"));

        if (output is null) return;
        Assert.Equal("900\n1\n901\n1\n", output);
    }

    [Fact]
    public void Switch_ReturnInsideArm_RunsEnclosingDefers()
    {
        var output = Run(CompileToIr(@"
func f(Option<i32> o) -> i32 {
    defer println(55)
    switch o {
        Some(v) => { return v }
        None => { println(0) }
    }
    return -1
}
func main() {
    println(f(Option<i32>.Some(3)))
    println(f(Option<i32>.None()))
}"));

        if (output is null) return;
        Assert.Equal("55\n3\n0\n55\n-1\n", output);
    }
}
