using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class RangeSliceCodegenTests
{
    private const string Show = @"
func show(i32[] s) {
    println(s.len)
    for i64 i = 0; i < s.len; i++ {
        switch s.get(i) {
            Some(v) => println(v)
            None => println(-1)
        }
    }
}
";

    [Fact]
    public void Range_OfArrayAndSlice_IsAViewWithOffsetAndLength()
    {
        var ir = CompileToIr(Show + @"
func main() {
    i32[6] a = [10, 11, 12, 13, 14, 15]
    i32[] s = a
    show(s[1..4])
    show(a[2..4])
    i32 lo = 1
    show(s[lo + 1 .. lo * 4])
}");

        Assert.Contains("range.sub.ptr", ir);
        Assert.Contains("range.fail", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("3\n11\n12\n13\n2\n12\n13\n2\n12\n13\n", output);
    }

    [Fact]
    public void Range_EmptyAndOpenBounds()
    {
        var output = Run(CompileToIr(Show + @"
func main() {
    i32[4] a = [1, 2, 3, 4]
    i32[] s = a
    i32 i = 2
    show(s[i..i])
    show(s[4..])
    show(s[..1])
    show(s[..])
    show(a[3..])
}"));

        if (output is null) return;
        Assert.Equal("0\n0\n1\n1\n4\n1\n2\n3\n4\n1\n4\n", output);
    }

    [Fact]
    public void Range_Chained_AndWithGetAndLen()
    {
        var output = Run(CompileToIr(Show + @"
func main() {
    i32[6] a = [0, 1, 2, 3, 4, 5]
    i32[] s = a
    show(s[1..5][1..3])
    println(s[2..6].len)
    switch s[2..6][1..].get(0) {
        Some(v) => println(v)
        None => println(-1)
    }
    switch s[2..4].get(2) {
        Some(v) => println(v)
        None => println(-1)
    }
}"));

        if (output is null) return;
        Assert.Equal("2\n2\n3\n4\n3\n-1\n", output);
    }

    [Fact]
    public void Range_SharesStorageWithTheArray()
    {
        var output = Run(CompileToIr(Show + @"
func main() {
    i32[3] a = [1, 2, 3]
    i32[] part = a[1..]
    a[2] = 30
    show(part)
}"));

        if (output is null) return;
        Assert.Equal("2\n2\n30\n", output);
    }

    [Theory]
    [InlineData("-1", "2")]
    [InlineData("3", "2")]
    [InlineData("1", "5")]
    [InlineData("0", "-1")]
    public void Range_OutOfBounds_AbortsAfterEarlierOutput(string start, string end)
    {
        var output = Run(CompileToIr(Show + $@"
func main() {{
    i32[4] a = [1, 2, 3, 4]
    i32[] s = a
    i32 lo = {start}
    i32 hi = {end}
    println(100)
    show(s[lo..hi])
    println(200)
}}"));

        if (output is null) return;
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void Range_IsBoundsCheckedOnArraysToo()
    {
        var output = Run(CompileToIr(Show + @"
func main() {
    i32[2] a = [1, 2]
    i32 hi = 3
    println(1)
    show(a[0..hi])
    println(2)
}"));

        if (output is null) return;
        Assert.Equal("1\n", output);
    }
}
