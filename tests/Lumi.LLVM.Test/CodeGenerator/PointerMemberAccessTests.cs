using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class PointerMemberAccessTests
{
    private const string Program = @"
struct Inner { i32 v }
struct Outer { i32 a i64 b Inner inner }
func get(*Outer p) -> *Outer { return p }
func main() -> void {
    Outer o = Outer { a: 1, b: 2, inner: Inner { v: 3 } }
    *Outer p = &o
    p.a = 10
    p.b = p.a as i64 + 5
    p.inner.v = 7
    p.a++
    println(p.a)
    println(p.b)
    println(p.inner.v)
    println(get(p).b)
    println(o.a)
}";

    [Fact]
    public void PointerMemberAccess_LowersToLoadedPointerAndStructGep_AndVerifies()
    {
        var ir = CompileToIr(Program);

        Assert.Contains("%Outer.a.ptr = getelementptr", ir);
        Assert.Contains("load ptr, ptr %p", ir);
    }

    [Fact]
    public void PointerMemberAccess_ReadsAndWritesTheOriginalStruct()
    {
        var output = Run(CompileToIr(Program));

        if (output is null) return;
        Assert.Equal("11\n15\n7\n15\n11\n", output.Replace("\r", ""));
    }

    [Fact]
    public void SequentialArenaAllocations_OfNestedStructs_DoNotAlias()
    {
        var output = Run(CompileToIr(@"
import std::mem
struct Vec2 { i32 x i32 y }
struct Entity { Vec2 pos i32 hp i32 id i32 flags }
func main() -> void {
    std::mem::Arena arena
    arena.init(128)
    *Entity ent = arena.alloc(sizeof(Entity), alignof(Entity)) as *Entity
    ent.pos.x = 1
    ent.pos.y = 2
    ent.hp = 100
    *Entity other = arena.alloc(sizeof(Entity), alignof(Entity)) as *Entity
    other.pos.x = 99
    other.pos.y = 88
    other.hp = 50
    println(sizeof(Entity))
    println(alignof(Entity))
    println(arena.head.offset)
    println(other as usize - ent as usize)
    println(ent.pos.x)
    println(ent.pos.y)
    println(ent.hp)
    println(other.pos.x)
    println(other.pos.y)
    println(other.hp)
}"));

        if (output is null) return;
        Assert.Equal("20\n4\n40\n20\n1\n2\n100\n99\n88\n50\n", output.Replace("\r", ""));
    }
}
