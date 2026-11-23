using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class UpdateExpressionCodegenTests
{
    [Fact]
    public void Increment_LowersToLoadAddStore_AndVerifies()
    {
        var ir = CompileToIr("func main() -> void { i32 x = 1\n x++ }");

        Assert.Contains("load i32, ptr %x", ir);
        Assert.Contains("add i32 %update.old, 1", ir);
        Assert.Contains("store i32 %update.inc, ptr %x", ir);
    }

    [Fact]
    public void Decrement_LowersToSub()
    {
        var ir = CompileToIr("func main() -> void { u8 x = 1\n --x }");

        Assert.Contains("sub i8 %update.old, 1", ir);
        Assert.Contains("store i8 %update.dec, ptr %x", ir);
    }

    [Fact]
    public void PrefixAndPostfix_YieldNewAndPreviousValues()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 i = 5
    i32 a = i++
    i32 b = ++i
    i32 c = i--
    i32 d = --i
    println(a)
    println(b)
    println(c)
    println(d)
    println(i)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal("5\n7\n7\n5\n5\n", output);
    }

    [Fact]
    public void StandaloneStatements_UpdateVariables()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i64 n = 10
    n++
    n++
    ++n
    --n
    n--
    println(n)
}"));

        if (output is null) return;
        Assert.Equal("11\n", output);
    }

    [Fact]
    public void UpdatesCanBeUsedInsideLargerExpressions()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 i = 1
    i32 r = i++ + ++i
    println(r)
    println(i)
}"));

        if (output is null) return;
        Assert.Equal("4\n3\n", output);
    }

    [Fact]
    public void Loops_CountWithPostfixAndPrefixSteps()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    for i32 i = 0; i < 3; i++ { print(i) }
    println("""")
    for i32 j = 3; j > 0; --j { print(j) }
    println("""")
    i32 k = 0
    while k++ < 3 { print(k) }
    println("""")
}"));

        if (output is null) return;
        Assert.Equal("012\n321\n123\n", output);
    }

    [Fact]
    public void StructFieldsAndArrayElements_AreUpdatedInPlace()
    {
        var output = Run(CompileToIr(@"
struct Counter { i32 hits u8 small }
struct Pair { Counter left }
func main() -> void {
    Counter c = Counter { hits: 1, small: 255 }
    Pair p = Pair { left: Counter { hits: 10, small: 0 } }
    i32[3] arr = [1, 2, 3]
    i32 old = c.hits++
    c.small++
    p.left.hits--
    arr[1]++
    ++arr[2]
    i32 idx = 0
    arr[idx++]--
    println(old)
    println(c.hits)
    println(c.small)
    println(p.left.hits)
    println(arr[0])
    println(arr[1])
    println(arr[2])
    println(idx)
}"));

        if (output is null) return;
        // c.small wraps from 255 to 0; arr[idx++] evaluates the index exactly once.
        Assert.Equal("1\n2\n0\n9\n0\n3\n4\n1\n", output);
    }

    [Fact]
    public void Parameters_AreUpdatedLocally()
    {
        var output = Run(CompileToIr(@"
func bump(i32 x) -> i32 {
    x++
    return ++x
}
func main() -> void { println(bump(1)) }"));

        if (output is null) return;
        Assert.Equal("3\n", output);
    }
}
