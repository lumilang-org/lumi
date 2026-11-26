using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class LetCodegenTests
{
    [Fact]
    public void Let_CompilesLikeExplicitlyTypedVariables()
    {
        var inferred = CompileToIr(@"
struct Pair { i32 a i32 b }
func main() -> void {
    let x = 20
    let flag = true
    let size = sizeof(Pair)
    let pair = Pair { a: 1, b: 2 }
}");
        var explicitIr = CompileToIr(@"
struct Pair { i32 a i32 b }
func main() -> void {
    i32 x = 20
    bool flag = true
    usize size = sizeof(Pair)
    Pair pair = Pair { a: 1, b: 2 }
}");

        Assert.Equal(explicitIr, inferred);
    }

    [Fact]
    public void Let_ProgramRunsWithExpectedOutput()
    {
        var output = Run(CompileToIr(@"
import std::mem
struct Entity { i32 id i32 hp }
func main() -> void {
    let x = 20
    let s = ""Sam""
    let flag = true
    println(x + 1)
    println(s)
    println(flag)

    std::mem::Arena arena
    arena.init(64)
    let raw = arena.alloc(sizeof(Entity), alignof(Entity))
    let e = raw as *Entity
    e.id = 7
    e.hp = 100
    let total = e.id + e.hp
    println(total)
    println(sizeof(Entity))
    let pair = Entity { id: 1, hp: 2 }
    println(pair.hp)
    i64 big = 5000000000
    println(big)
    arena.destroy()
}"));

        if (output is null) return;
        Assert.Equal("21\nSam\ntrue\n107\n8\n2\n5000000000\n", output.Replace("\r", ""));
    }
}
