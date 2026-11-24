using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class CastCodegenTests
{
    [Fact]
    public void PointerCast_VerifiesAndNeedsNoInstruction()
    {
        var ir = CompileToIr(@"
func main() -> void {
    *u8 p = nil
    *u64 q = p as *u64
    *u8 r = q as *u8
}");

        Assert.DoesNotContain("bitcast", ir);
        Assert.DoesNotContain("ptrtoint", ir);
    }

    [Fact]
    public void PointerIntegerCasts_EmitPtrToIntAndIntToPtr()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 x = 1
    *i32 p = &x
    usize a = p as usize
    *i32 q = a as *i32
}");

        Assert.Contains("ptrtoint ptr", ir);
        Assert.Contains("inttoptr i64", ir);
    }

    [Fact]
    public void IntegerCasts_TruncateExtendWithSignedness()
    {
        var ir = CompileToIr(@"
func main() -> void {
    i32 a = 300
    u8 b = a as u8
    i8 c = -1
    i64 d = c as i64
    u8 e = 255
    i64 f = e as i64
}");

        Assert.Contains("trunc i32", ir);
        Assert.Contains("sext i8", ir);
        Assert.Contains("zext i8", ir);
    }

    [Fact]
    public void IntegerCasts_RunWithExpectedValues()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    i32 a = 300
    println(a as u8)
    i8 c = -1
    println(c as i64)
    u8 e = 255
    println(e as i64)
    println(-1 as u8)
}"));

        if (output is null) return;
        Assert.Equal("44\n-1\n255\n255\n", output.Replace("\r", ""));
    }

    [Fact]
    public void StructThroughCastPointer_WritesAndReads()
    {
        var ir = CompileToIr(@"
struct Pair { i32 a i64 b }
extern func malloc(usize size) -> *u8
func main() -> void {
    *Pair p = malloc(16) as *Pair
    (*p).a = 7
    (*p).b = 9
    println((*p).a)
    println((*p).b)
    *u64 w = p as *u64
    *w = 5
    println(*w)
}");

        Assert.Contains("define", ir);
        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("7\n9\n5\n", output.Replace("\r", ""));
    }
}
