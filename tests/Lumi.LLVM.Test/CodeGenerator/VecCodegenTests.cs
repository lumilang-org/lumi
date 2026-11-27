using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class VecCodegenTests
{
    [Fact]
    public void StdVec_PushPopAndSlice_WithArenaAndDefer()
    {
        var ir = CompileToIr(@"
import std::vec

func main() {
    Arena arena

    if !arena.init(4096) {
        return
    }

    defer arena.destroy()

    Vec<i32> nums

    if !nums.init(&arena, 0) {
        return
    }

    defer nums.deinit()

    nums.push(10)
    nums.push(20)
    nums.push(30)

    i32[] view = nums.as_slice()
    i32[] sub = view[1..3]

    switch sub.get(0) {
        Some(val) => println(val)
        None => {}
    }

    switch nums.pop() {
        Some(last) => { println(last) }
        None => {}
    }
}");

        Assert.Contains("%Vec__i32 = type", ir);
        Assert.Contains("@Vec__i32__push(", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("20\n30\n", output);
    }

    [Fact]
    public void StdVec_GrowsPastItsCapacity_AndPopsEverything()
    {
        var output = Run(CompileToIr(@"
import std::vec

func main() {
    Arena arena
    arena.init(64)
    defer arena.destroy()

    Vec<i64> v
    v.init(&arena, 2)

    for i64 i = 1; i <= 20; i++ {
        v.push(i * i)
    }

    i64 total = 0
    for x in v.as_slice() { total = total + x }
    println(total)
    println(v.len)

    v.pop()
    v.pop()
    println(v.len)
    switch v.as_slice().get(17) {
        Some(x) => println(x)
        None => println(-1)
    }
    switch v.as_slice().get(18) {
        Some(x) => println(x)
        None => println(-1)
    }

    Vec<i32> empty
    empty.init(&arena, 0)
    switch empty.pop() {
        Some(x) => println(x)
        None => println(0)
    }
}"));

        if (output is null) return;
        Assert.Equal("2870\n20\n18\n324\n-1\n0\n", output);
    }

    [Fact]
    public void StdVec_OfStructs_AndTwoElementTypes()
    {
        var output = Run(CompileToIr(@"
import std::vec

struct Vec2 { pub i32 x pub i32 y }

func main() {
    Arena arena
    arena.init(256)
    defer arena.destroy()

    Vec<Vec2> pts
    pts.init(&arena, 0)
    pts.push(Vec2 { x: 1, y: 2 })
    pts.push(Vec2 { x: 3, y: 4 })

    Vec<u8> bytes
    bytes.init(&arena, 0)
    bytes.push(9)

    for p in pts.as_slice() { println(p.x + p.y) }
    println(bytes.len)
}"));

        if (output is null) return;
        Assert.Equal("3\n7\n1\n", output);
    }

    [Fact]
    public void GenericsFromAnotherModule_AreUsableByName()
    {
        var output = Run(CompileToIr(@"
import shapes

func main() {
    Box<i32> b = wrap<i32>(41)
    println(b.value + 1)
    Box<Box<u8>> nested = wrap<Box<u8>>(wrap<u8>(5))
    println(nested.value.value)
    Option<i32> o = Option<i32>.Some(1)
    println(b.get())
}", ("shapes.lumi", @"
module shapes

pub struct Box<T> {
    pub T value

    pub func get(*Box<T> self) -> T { return self.value }
}

pub func wrap<T>(T v) -> Box<T> {
    return Box<T> { value: v }
}
")));

        if (output is null) return;
        Assert.Equal("42\n5\n41\n", output);
    }

    [Fact]
    public void BareVariants_InferTheirEnumFromContext()
    {
        var output = Run(CompileToIr(@"
func first(i32[] s) -> Option<i32> {
    if s.len == 0 { return None }
    return Some(s[0])
}
func check(i32 n) -> Result<i32, bool> {
    if n < 0 { return Err(false) }
    return Ok(n)
}
func show(Option<i32> o) {
    switch o {
        Some(v) => println(v)
        None => println(-1)
    }
}
func main() {
    i32[2] a = [7, 8]
    show(first(a))
    show(first(a[0..0]))
    show(Some(5))
    show(None)
    Option<i32> o = Some(3)
    Option<i32> n = None()
    show(o)
    show(n)
    switch check(-1) {
        Ok(v) => println(v)
        Err(e) => println(e)
    }
    switch check(4) {
        Ok(v) => println(v)
        Err(e) => println(e)
    }
}"));

        if (output is null) return;
        Assert.Equal("7\n-1\n5\n-1\n3\n-1\nfalse\n4\n", output);
    }

    [Fact]
    public void PointerRange_BuildsASliceWithoutALengthCheck()
    {
        var output = Run(CompileToIr(@"
import std::mem

func main() {
    Arena arena
    arena.init(64)
    defer arena.destroy()

    *i32 p = arena.alloc(16, 4) as *i32
    for i32 i = 0; i < 4; i++ { p[i] = i * 10 }
    i32[] s = p[1..3]
    println(s.len)
    println(s[0])
    println(s[1])
    i32[] t = p[2..2]
    println(t.len)
    i32 lo = 3
    i32 hi = 1
    println(100)
    i32[] bad = p[lo..hi]
    println(200)
}"));

        if (output is null) return;
        Assert.Equal("2\n10\n20\n0\n100\n", output);
    }
}
