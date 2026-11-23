using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class UsizeCodegenTests
{
    // usize is lowered to the target's pointer-width integer.
    private static readonly string PointerWidthType = $"i{IntPtr.Size * 8}";

    [Fact]
    public void Usize_IsLoweredToPointerWidthInteger_AndVerifies()
    {
        var ir = CompileToIr(@"
struct Slice { usize start usize end }
func twice(usize n) -> usize { return n + n }
func main() -> void {
    usize n = 5
    usize[2] sizes = [1, 2]
    Slice s = Slice { start: 1, end: 2 }
    usize r = twice(n)
}");

        Assert.Contains($"define {PointerWidthType} @twice({PointerWidthType} %0)", ir);
        Assert.Contains($"%n = alloca {PointerWidthType}", ir);
        Assert.Contains($"%sizes = alloca [2 x {PointerWidthType}]", ir);
        Assert.Contains($"%s = alloca %Slice", ir);
        Assert.Contains($"%Slice = type {{ {PointerWidthType}, {PointerWidthType} }}", ir);
    }

    [Fact]
    public void UnsignedOperations_AreEmittedForUsize()
    {
        var ir = CompileToIr(@"
func main() -> void {
    usize a = 6
    usize b = 3
    bool lt = a < b
    bool le = a <= b
    bool gt = a > b
    bool ge = a >= b
    usize q = a / b
    usize r = a >> 1
}");

        Assert.Contains("icmp ult", ir);
        Assert.Contains("icmp ule", ir);
        Assert.Contains("icmp ugt", ir);
        Assert.Contains("icmp uge", ir);
        Assert.Contains("udiv", ir);
        Assert.Contains("lshr", ir);
        Assert.DoesNotContain("sdiv", ir);
        Assert.DoesNotContain("icmp slt", ir);
        Assert.DoesNotContain("ashr", ir);
    }

    [Fact]
    public void Arithmetic_AndPrinting()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    usize a = 20
    usize b = 6
    println(a + b)
    println(a - b)
    println(a * b)
    println(a / b)
    println(a & b)
    println(a | b)
    println(a ^ b)
    println(a << 2)
    println(a >> 2)
    print(a)
    print(b)
    println(0)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal("26\n14\n120\n3\n4\n22\n18\n80\n5\n2060\n", output);
    }

    [Fact]
    public void UnsignedSemantics_ForLargeValues()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    usize zero = 0
    usize max = zero - 1
    println(max)
    println(max > 1)
    println(max < 1)
    println(max / 2)
    println(max >> 63)
    println(~zero == max)
    println(max + 1)
    usize big = 18446744073709551615
    println(big == max)
}"));

        if (output is null) return;
        Assert.Equal(
            "18446744073709551615\ntrue\nfalse\n9223372036854775807\n1\ntrue\n0\ntrue\n",
            output);
    }

    [Fact]
    public void Comparisons()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    usize a = 3
    usize b = 5
    println(a == b)
    println(a != b)
    println(a < b)
    println(a <= b)
    println(a > b)
    println(a >= b)
    println(a >= 3)
}"));

        if (output is null) return;
        Assert.Equal("false\ntrue\ntrue\ntrue\nfalse\nfalse\ntrue\n", output);
    }

    [Fact]
    public void NarrowerUnsignedOperands_AreZeroExtended()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    usize n = 1000
    u8 small = 200
    u16 medium = 60000
    u32 large = 4000000000
    println(n + small)
    println(n + medium)
    println(large + n)
    println(small < n)
    println(large > n)
}"));

        if (output is null) return;
        Assert.Equal("1200\n61000\n4000001000\ntrue\ntrue\n", output);
    }

    [Fact]
    public void IncrementAndDecrement()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    usize n = 5
    usize a = n++
    usize b = ++n
    n--
    println(a)
    println(b)
    println(n)
}"));

        if (output is null) return;
        Assert.Equal("5\n7\n6\n", output);
    }

    [Fact]
    public void LoopsStructsArraysFunctionsAndIndexing()
    {
        var output = Run(CompileToIr(@"
struct Range { usize start usize end }
func width(Range r) -> usize { return r.end - r.start }
func sum(usize[4] values) -> usize {
    usize total = 0
    for usize i = 0; i < 4; i++ { total = total + values[i] }
    return total
}
func main() -> void {
    Range r = Range { start: 3, end: 10 }
    println(width(r))
    usize[4] values = [10, 20, 30, 40]
    println(sum(values))
    usize i = 2
    println(values[i])
    values[i] = values[i] * 2
    println(values[2])
    r.end = r.end + width(r)
    println(r.end)
}"));

        if (output is null) return;
        Assert.Equal("7\n100\n30\n60\n17\n", output);
    }

    [Fact]
    public void Pointers_ToUsize()
    {
        var output = Run(CompileToIr(@"
func grow(*usize p) -> void { *p = *p + 10 }
func main() -> void {
    usize n = 1
    grow(&n)
    *usize p = &n
    *p = *p * 2
    println(n)
}"));

        if (output is null) return;
        Assert.Equal("22\n", output);
    }

    [Fact]
    public void Print_UnsignedValuesAreNotPrintedAsSigned()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    u8 a = 200
    u16 b = 60000
    u32 c = 4000000000
    u64 d = 18446744073709551615
    i8 e = -100
    i64 f = -9223372036854775807
    println(a)
    println(b)
    println(c)
    println(d)
    println(e)
    println(f)
}"));

        if (output is null) return;
        Assert.Equal("200\n60000\n4000000000\n18446744073709551615\n-100\n-9223372036854775807\n", output);
    }
}
