using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class NilCodegenTests
{
    [Fact]
    public void Nil_LowersToANullPointerConstant_AndVerifies()
    {
        var ir = CompileToIr(@"
func main() -> void {
    *i32 p = nil
    p = nil
}");

        Assert.Equal(2, CountOccurrences(ir, "store ptr null, ptr %p"));
    }

    [Fact]
    public void PointerNilComparisons_LowerToPointerIcmp_AndVerify()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    bool a = p == nil
    bool b = p != nil
    bool c = nil == p
    bool d = nil != p
    bool e = nil == nil
}");

        // p == nil and nil == p; p != nil and nil != p. nil == nil folds to a constant.
        Assert.Equal(2, CountOccurrences(ir, "icmp eq ptr"));
        Assert.Equal(2, CountOccurrences(ir, "icmp ne ptr"));
        Assert.Contains(", null", ir);
        Assert.Contains("icmp eq ptr null, %", ir);
        Assert.Contains("store i1 true", ir);
    }

    [Fact]
    public void NilComparisons_ProduceTheExpectedBooleans()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    *i32 n = nil
    println(p == nil)
    println(p != nil)
    println(nil == p)
    println(nil != p)
    println(n == nil)
    println(n != nil)
    println(nil == n)
    println(nil == nil)
    println(nil != nil)
    println(n == p)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal("false\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Nil_FlowsThroughVariablesAssignmentsParametersReturnsStructsAndArrays()
    {
        var output = Run(CompileToIr(@"
struct Node { i32 value *Node next }
func isNil(*i32 p) -> bool { return p == nil }
func none() -> *i32 { return nil }
func pick(bool first, *i32 a) -> *i32 {
    if (first) { return a }
    return nil
}
func main() -> void {
    i32 x = 5
    *i32 p = &x
    **i32 pp = &p
    println(isNil(p))
    println(isNil(nil))
    println(isNil(none()))
    println(pick(true, &x) == nil)
    println(pick(false, &x) == nil)
    *pp = nil
    println(p == nil)
    p = &x
    println(p == nil)

    Node n = Node { value: 1, next: nil }
    println(n.next == nil)
    Node m = Node { value: 2, next: &n }
    println(m.next == nil)
    m.next = nil
    println(m.next == nil)

    *i32[2] arr = [nil, &x]
    println(arr[0] == nil)
    println(arr[1] == nil)
    arr[1] = nil
    println(arr[1] == nil)
}"));

        if (output is null) return;
        Assert.Equal(
            "false\ntrue\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\n",
            output);
    }

    [Fact]
    public void NilPointers_PrintAsAddressZero()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    *i32 p = nil
    println(p)
}"));

        if (output is null) return;
        Assert.Equal("0x0\n", output);
    }

    [Fact]
    public void PointerOffsets_AreScaledByThePointeeSize_AndVerify()
    {
        var ir = CompileToIr(@"
struct Pair { i32 a i32 b }
func main() -> void {
    i32[4] ints = [1, 2, 3, 4]
    *i32 pi = &ints[0]
    *i32 a = pi + 2
    *u8 pb = nil
    *u8 b = pb + 3
    *Pair pp = nil
    *Pair c = pp + 1
}");

        Assert.Contains("getelementptr i32, ptr", ir);
        Assert.Contains("getelementptr i8, ptr", ir);
        Assert.Contains("getelementptr %Pair, ptr", ir);
    }

    [Fact]
    public void PointerOffsets_ReadAndWriteArrayElements()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32[4] arr = [10, 20, 30, 40]
    *i32 p = &arr[0]
    println(*(p + 1))
    println(*(2 + p))
    *(p + 3) = 99
    println(arr[3])

    *i32 end = p + 3
    println(*(end - 1))
    println(*(end - 3))

    usize n = 2
    i32 back = -1
    println(*(p + n))
    println(*(end + back))
    println(*(end - back + back))
}"));

        if (output is null) return;
        Assert.Equal("20\n30\n99\n30\n10\n30\n30\n99\n", output);
    }

    [Fact]
    public void PointerOffsets_WalkAnArrayInALoop()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32[5] arr = [1, 2, 3, 4, 5]
    *i32 p = &arr[0]
    i32 total = 0
    for usize i = 0; i < 5; i++ {
        total = total + *(p + i)
    }
    println(total)
    for usize i = 0; i < 5; i++ {
        *(p + i) = *(p + i) * 2
    }
    println(arr[0])
    println(arr[4])
}"));

        if (output is null) return;
        Assert.Equal("15\n2\n10\n", output);
    }

    [Fact]
    public void PointerOffsets_MoveByWholeElements_NotBytes()
    {
        var output = Run(CompileToIr(@"
struct Pair { i32 a i32 b }
func main() -> void {
    i32[3] ints = [1, 2, 3]
    u8[3] bytes = [1, 2, 3]
    Pair[2] pairs = [Pair { a: 1, b: 2 }, Pair { a: 3, b: 4 }]
    *i32 pi = &ints[0]
    *u8 pb = &bytes[0]
    *Pair pp = &pairs[0]
    println(*(pi + 2))
    println(*(pb + 2))
    println((*(pp + 1)).b)
}"));

        if (output is null) return;
        Assert.Equal("3\n3\n4\n", output);

        var addresses = Run(CompileToIr(@"
struct Pair { i32 a i32 b }
func main() -> void {
    i32[3] ints = [1, 2, 3]
    Pair[2] pairs = [Pair { a: 1, b: 2 }, Pair { a: 3, b: 4 }]
    *i32 pi = &ints[0]
    *Pair pp = &pairs[0]
    println(pi)
    println(pi + 1)
    println(pp)
    println(pp + 1)
}"))!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Convert.ToUInt64(line[2..], 16))
            .ToArray();

        Assert.Equal(4UL, addresses[1] - addresses[0]);
        Assert.Equal(8UL, addresses[3] - addresses[2]);
    }

    [Fact]
    public void PointerOffsets_WorkWithNegativeAndUnsignedOffsetsOfAnyWidth()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32[4] arr = [10, 20, 30, 40]
    *i32 p = &arr[2]
    i8 a = -2
    i16 b = -1
    i64 c = 1
    u8 d = 1
    u16 e = 1
    u32 f = 1
    println(*(p + a))
    println(*(p + b))
    println(*(p + c))
    println(*(p + d))
    println(*(p + e))
    println(*(p + f))
    println(*(p - d))
}"));

        if (output is null) return;
        Assert.Equal("10\n20\n40\n40\n40\n40\n20\n", output);
    }
}
