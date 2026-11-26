using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class SliceCodegenTests
{
    [Fact]
    public void Slice_LowersToPointerAndLength_DistinctFromArrays()
    {
        var ir = CompileToIr(@"
func size(i32[] s) -> i64 { return s.len }
func main() {
    i32[3] a = [1, 2, 3]
    println(size(a))
}");

        Assert.Contains("{ ptr, i64 }", ir);
        Assert.Contains("[3 x i32]", ir);
    }

    [Fact]
    public void Len_OfSliceAndArray()
    {
        var output = Run(CompileToIr(@"
func size(i32[] s) -> i64 { return s.len }
func main() {
    i32[4] a = [1, 2, 3, 4]
    i32[] s = a
    println(s.len)
    println(a.len)
    println(size([7, 8]))
    i32[] empty
    println(empty.len)
}"));

        if (output is null) return;
        Assert.Equal("4\n4\n2\n0\n", output);
    }

    [Fact]
    public void Get_InAndOutOfBounds_WithSwitch()
    {
        var output = Run(CompileToIr(@"
func at(i32[] s, i64 i) {
    switch s.get(i) {
        Some(v) => println(v)
        None => println(-1)
    }
}
func main() {
    i32[3] a = [10, 20, 30]
    i32[] s = a
    at(s, 0)
    at(s, 2)
    at(s, 3)
    at(s, -1)
    switch a.get(1) {
        Option.Some(v) => println(v + 1)
        Option.None => println(0)
    }
}"));

        if (output is null) return;
        Assert.Equal("10\n30\n-1\n-1\n21\n", output);
    }

    [Fact]
    public void Slice_SeesLaterWritesToTheArray()
    {
        var output = Run(CompileToIr(@"
func main() {
    i32[2] a = [1, 2]
    i32[] s = a
    a[1] = 99
    switch s.get(1) {
        Some(v) => println(v)
        None => println(0)
    }
}"));

        if (output is null) return;
        Assert.Equal("99\n", output);
    }

    [Fact]
    public void Slices_OfOtherElementTypes_AndNestedInOption()
    {
        var output = Run(CompileToIr(@"
struct Vec2 { pub i32 x pub i32 y }
func main() {
    Vec2[2] pts = [Vec2 { x: 1, y: 2 }, Vec2 { x: 3, y: 4 }]
    Vec2[] s = pts
    switch s.get(1) {
        Some(p) => println(p.x + p.y)
        None => println(0)
    }
    u8[3] bytes = [1, 2, 3]
    u8[] bs = bytes
    switch bs.get(200) {
        Some(b) => println(b)
        None => println(-5)
    }
    Option<i32[]> o = Option<i32[]>.Some([4, 5, 6])
    switch o {
        Some(inner) => println(inner.len)
        None => println(0)
    }
}"));

        if (output is null) return;
        Assert.Equal("7\n-5\n3\n", output);
    }

    [Fact]
    public void Get_WithDeferAndEarlyReturn()
    {
        var output = Run(CompileToIr(@"
func pick(i32[] s, i64 i) -> i32 {
    defer println(77)
    switch s.get(i) {
        Some(v) => { return v }
        None => { return -1 }
    }
}
func main() {
    println(pick([5, 6], 1))
    println(pick([5, 6], 9))
}"));

        if (output is null) return;
        Assert.Equal("77\n6\n77\n-1\n", output);
    }

    [Fact]
    public void Index_ReadsAndWritesThroughTheView()
    {
        var output = Run(CompileToIr(@"
func bump(i32[] s) {
    s[0] = s[0] + 100
    s[s.len - 1] = 7
}
func main() {
    i32[3] a = [1, 2, 3]
    i32[] s = a
    println(s[1])
    bump(s)
    println(a[0])
    println(a[2])
    i32[] mid = s[1..3]
    mid[0] = 55
    println(a[1])
    println(s[0..2][1])
    u8 i = 2
    println(s[i])
}"));

        if (output is null) return;
        Assert.Equal("2\n101\n7\n55\n55\n7\n", output);
    }

    [Fact]
    public void Index_OfSliceOfSlices_AndStructElements()
    {
        var output = Run(CompileToIr(@"
struct Vec2 { pub i32 x pub i32 y }
func main() {
    i32[2] a = [1, 2]
    i32[2] b = [3, 4]
    i32[][2] rows = [a, b]
    println(rows[1][0] + rows[0][1])
    Vec2[2] pts = [Vec2 { x: 1, y: 2 }, Vec2 { x: 3, y: 4 }]
    Vec2[] ps = pts
    ps[1].y = 40
    println(ps[1].x + ps[1].y)
}"));

        if (output is null) return;
        Assert.Equal("5\n43\n", output);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("100")]
    public void Index_OutOfBounds_AbortsAfterEarlierOutput(string index)
    {
        var output = Run(CompileToIr($@"
func main() {{
    i32[3] a = [1, 2, 3]
    i32[] s = a
    i32 i = {index}
    println(1)
    println(s[i])
    println(2)
}}"));

        if (output is null) return;
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void ForIn_OverSlices_IncludingRangesBreakAndContinue()
    {
        var output = Run(CompileToIr(@"
func total(i32[] s) -> i32 {
    i32 sum = 0
    for x in s { sum = sum + x }
    return sum
}
func main() {
    i32[5] a = [1, 2, 3, 4, 5]
    i32[] s = a
    println(total(s))
    println(total(s[1..4]))
    println(total(s[2..2]))
    for x in s[3..] { println(x) }
    for x in s {
        if x == 2 { continue }
        if x == 4 { break }
        println(x)
    }
    for x in [7, 8] { println(x) }
    i32[] empty
    for x in empty { println(-1) }
}"));

        if (output is null) return;
        Assert.Equal("15\n9\n0\n4\n5\n1\n3\n7\n8\n", output);
    }

    [Fact]
    public void ForIn_OverSlice_WithDefer()
    {
        var output = Run(CompileToIr(@"
func main() {
    i32[] s = [1, 2]
    for x in s {
        defer println(90 + x)
        println(x)
    }
}"));

        if (output is null) return;
        Assert.Equal("1\n91\n2\n92\n", output);
    }
}
