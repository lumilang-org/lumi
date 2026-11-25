using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class PointerCodegenTests
{
    [Fact]
    public void AddressOfAndDereference_LowerToPointerLoadsAndStores_AndVerify()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    *p = 2
    i32 y = *p
}");

        Assert.Contains("alloca ptr", ir);
        Assert.Contains("store ptr %x, ptr %p", ir);
        Assert.Contains("store i32 2, ptr %", ir);
        Assert.Contains("load i32, ptr %", ir);
    }

    [Fact]
    public void ReadThroughPointer()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 41
    *i32 p = &x
    println(*p)
    println(*p + 1)
    println(2 * *p)
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal("41\n42\n82\n", output);
    }

    [Fact]
    public void MutateThroughPointer()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    *p = 10
    *p = *p + 5
    (*p)++
    println(x)
}"));

        if (output is null) return;
        Assert.Equal("16\n", output);
    }

    [Fact]
    public void StarOnANewLine_StartsAnAssignmentInsteadOfMultiplying()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    i32 y = 2
    *p = 7
    println(x)
}"));

        if (output is null) return;
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void PointersAsParametersAndReturns()
    {
        var output = Run(CompileToIr(@"
func bump(*i32 p) -> void {
    *p = *p + 1
}
func same(*i32 p) -> *i32 {
    return p
}
func main() -> void {
    i32 n = 1
    bump(&n)
    bump(same(&n))
    println(n)
}"));

        if (output is null) return;
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void SwapThroughPointers()
    {
        var output = Run(CompileToIr(@"
func swap(*i32 a, *i32 b) -> void {
    i32 t = *a
    *a = *b
    *b = t
}
func main() -> void {
    i32 x = 1
    i32 y = 2
    swap(&x, &y)
    println(x)
    println(y)
}"));

        if (output is null) return;
        Assert.Equal("2\n1\n", output);
    }

    [Fact]
    public void ChainedDereferences()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 5
    *i32 p = &x
    **i32 pp = &p
    **pp = 6
    println(x)
    println(**pp)
    *pp = &x
    println(*p == x)
}"));

        if (output is null) return;
        Assert.Equal("6\n6\ntrue\n", output);
    }

    [Fact]
    public void StructPointers_FieldsThroughParenthesisedDereference()
    {
        var output = Run(CompileToIr(@"
struct Point { i32 x i32 y }
func move(*Point p, i32 dx) -> void {
    (*p).x = (*p).x + dx
    (*p).y++
}
func main() -> void {
    Point pt = Point { x: 1, y: 1 }
    move(&pt, 10)
    *i32 px = &pt.x
    *px = *px * 2
    println(pt.x)
    println(pt.y)
    Point copy = *&pt
    println(copy.x)
}"));

        if (output is null) return;
        Assert.Equal("22\n2\n22\n", output);
    }

    [Fact]
    public void ArrayElementAndWholeArrayPointers()
    {
        var output = Run(CompileToIr(@"
func fill(*(i32[3]) a) -> void {
    for i32 i = 0; i < 3; i++ {
        (*a)[i] = i * 10
    }
}
func main() -> void {
    i32[3] arr = [1, 2, 3]
    *i32 second = &arr[1]
    *second = 20
    println(arr[1])
    fill(&arr)
    println(arr[0])
    println(arr[1])
    println(arr[2])
    *(i32[3]) whole = &arr
    println((*whole)[2])
}"));

        if (output is null) return;
        Assert.Equal("20\n0\n10\n20\n20\n", output);
    }

    [Fact]
    public void ArrayOfPointers_AndSelfReferentialStructs()
    {
        var output = Run(CompileToIr(@"
struct Node { i32 value *Node next }
func main() -> void {
    i32 a = 1
    i32 b = 2
    *i32[2] ptrs = [&a, &b]
    *(ptrs[1]) = 20
    println(b)

    Node first
    Node second
    first.value = 1
    second.value = 2
    first.next = &second
    second.next = &first
    println((*first.next).value)
    println((*(*first.next).next).value)
    *i32 v = &(*first.next).value
    *v = 3
    println(second.value)
}"));

        if (output is null) return;
        Assert.Equal("20\n2\n1\n3\n", output);
    }

    [Fact]
    public void StringAndCharPointers()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    string s = ""hi""
    *string ps = &s
    *ps = ""there""
    println(s)
    char c = 'a'
    *char pc = &c
    *pc = 'z'
    println(c)
    *char first = &s[0]
    println(*first)
}"));

        if (output is null) return;
        Assert.Equal("there\nz\nt\n", output);
    }

    [Fact]
    public void PointerEquality()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 a = 1
    i32 b = 1
    *i32 p = &a
    *i32 q = &a
    println(p == q)
    println(p != &b)
    println(&a == &b)
}"));

        if (output is null) return;
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Fact]
    public void PointersAcrossModules()
    {
        var output = Run(CompileToIr(@"
import util
func main() -> void {
    util::Box b = util::Box { n: 1 }
    util::inc(&b)
    println(b.n)
}",
            ("util.lumi", @"
module util
pub struct Box { pub i32 n }
pub func inc(*Box b) -> void { (*b).n++ }")));

        if (output is null) return;
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void PrintingPointers_EmitsPtrToIntAndSnprintf_AndVerifies()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    print(p)
    println(&x)
}");

        Assert.Equal(2, CountOccurrences(ir, "ptrtoint ptr"));
        Assert.Contains("0x%llx", ir);
        Assert.Contains("declare i32 @snprintf", ir);
        // The pointee is never read: only the pointer itself is loaded.
        Assert.DoesNotContain("load i32", ir);
    }

    [Fact]
    public void PrintAndPrintln_WritePointerAddresses()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    print(p)
    print(p)
    println(p)
}"));

        if (output is null) return; // lli unavailable
        Assert.Matches("^(0x[0-9a-f]+){2}0x[0-9a-f]+\n$", output);

        var address = output.TrimEnd('\n')[..(output.TrimEnd('\n').Length / 3)];
        Assert.Equal(address + address + address + "\n", output);
    }

    [Fact]
    public void PrintedAddress_IsTheAddressNotThePointee()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 a = 1
    i32 b = 1
    *i32 p = &a
    *i32 q = &a
    println(p)
    println(q)
    println(&a)
    println(&b)
    println(*p)
}"));

        if (output is null) return;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        Assert.Matches("^0x[0-9a-f]+$", lines[0]);
        Assert.Equal(lines[0], lines[1]);
        Assert.Equal(lines[0], lines[2]);
        Assert.NotEqual(lines[0], lines[3]);
        Assert.Equal("1", lines[4]);
    }

    [Fact]
    public void PrintingPointers_OfEveryPointeeKind()
    {
        var output = Run(CompileToIr(@"
struct P { i32 x i32 y }
func show(*u8 p) -> void { println(p) }
func main() -> void {
    u8 b = 7
    P pt = P { x: 1, y: 2 }
    i32[2] arr = [1, 2]
    string s = ""hi""
    *u8 pb = &b
    *P pp = &pt
    **P ppp = &pp
    *(i32[2]) pa = &arr
    *string ps = &s
    println(pb)
    println(pp)
    println(ppp)
    println(pa)
    println(ps)
    println(&pt.y)
    println(&arr[1])
    show(&b)
    show(pb)
    println(*pb)
}"));

        if (output is null) return;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10, lines.Length);
        foreach (var line in lines.Take(9))
        {
            Assert.Matches("^0x[0-9a-f]+$", line);
        }

        // show(&b) and show(pb) print the same address as pb.
        Assert.Equal(lines[0], lines[7]);
        Assert.Equal(lines[0], lines[8]);
        Assert.Equal("7", lines[9]);
    }

    [Fact]
    public void PrintedAddresses_FollowMemoryLayout()
    {
        var output = Run(CompileToIr(@"
struct P { i32 x i32 y }
func main() -> void {
    P pt = P { x: 1, y: 2 }
    i32[3] arr = [1, 2, 3]
    println(&arr[0])
    println(&arr[1])
    println(&arr[2])
    println(&pt.x)
    println(&pt.y)
}"));

        if (output is null) return;
        var addresses = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Convert.ToUInt64(line[2..], 16))
            .ToArray();

        Assert.Equal(4UL, addresses[1] - addresses[0]);
        Assert.Equal(4UL, addresses[2] - addresses[1]);
        Assert.Equal(4UL, addresses[4] - addresses[3]);
    }
}
