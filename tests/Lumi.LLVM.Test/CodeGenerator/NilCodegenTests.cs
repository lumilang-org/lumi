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

    [Fact]
    public void PointerPlusUsize_SteppesAcrossContiguousStructs()
    {
        var ir = CompileToIr(@"
struct Vec2 { i32 x i32 y }
struct Entity { Vec2 pos Vec2 vel i32 hp }
func main() -> void {
    Entity[3] items
    *Entity base = &items[0]
    usize one = 1
    usize two = 2
    (base + 0).hp = 10
    (base + one).hp = 20
    (base + two).hp = 30
    (base + one).pos.x = 5
    println(sizeof(Entity))
    println((base + 0).hp)
    println((base + one).hp)
    println((base + two).hp)
    println(items[1].hp)
    println(items[1].pos.x)
    println((base + two) as usize - base as usize)
}");

        Assert.Contains("getelementptr %Entity, ptr", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("20\n10\n20\n30\n20\n5\n40\n", output.Replace("\r", ""));
    }

    [Fact]
    public void PointerPlusUsize_OnBytesAndNestedPointers()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    arena.init(64)
    *u8 a = arena.alloc(24, 8)
    usize off = 16
    *u8 b = a + off
    println(b as usize - a as usize)

    i32 x = 1
    *i32[2] slots = [nil, nil]
    **i32 pp = &slots[0]
    usize one = 1
    println((pp + one) as usize - pp as usize)
    arena.destroy()
}"));

        if (output is null) return;
        Assert.Equal("16\n8\n", output.Replace("\r", ""));
    }

    [Fact]
    public void PointerIndexing_ReadsWritesAndCopiesStructElements()
    {
        var ir = CompileToIr(@"
struct Item { i32 id i32 qty i32 x i32 y }
struct Room { *Item items usize count }
func main() -> void {
    Item[3] storage
    Room room = Room { items: &storage[0], count: 0 }
    *Room r = &room
    let buf = r.items
    usize i = 0
    while i < 3 {
        buf[i].id = i as i32 + 1
        buf[i].qty = 10
        i = i + 1
    }
    buf[2] = buf[0]
    buf[1].qty++
    r.items[r.count + 1].x = 5
    let n: i64 = 1
    println(buf[0].id)
    println(buf[1].id)
    println(buf[2].id)
    println(buf[1].qty)
    println(r.items[1].x)
    println(storage[2].qty)
    println(sizeof(Item))
    println(&buf[2] as usize - &buf[0] as usize)
    println(buf[n].id)
}");

        Assert.Contains("ptr_index_ptr", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("1\n2\n1\n11\n5\n10\n16\n32\n2\n", output.Replace("\r", ""));
    }

    [Fact]
    public void PointerIndexing_ScalarsAndArenaBuffers()
    {
        var output = Run(CompileToIr(@"
import std::mem
func main() -> void {
    std::mem::Arena arena
    arena.init(64)
    let nums = arena.alloc(sizeof(i64) * 4, alignof(i64)) as *i64
    usize i = 0
    while i < 4 {
        nums[i] = (i as i64) * 100
        i = i + 1
    }
    let bytes = nums as *u8
    bytes[0] = 7
    println(nums[0])
    println(nums[3])
    println(bytes[0])
    println(nums[1] + nums[2])
    arena.destroy()
}"));

        if (output is null) return;
        Assert.Equal("7\n300\n7\n300\n", output.Replace("\r", ""));
    }
}
