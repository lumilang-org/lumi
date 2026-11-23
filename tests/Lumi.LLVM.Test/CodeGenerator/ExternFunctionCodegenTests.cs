using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class ExternFunctionCodegenTests
{
    [Fact]
    public void ExternFunction_IsDeclaredWithoutABody_AndCalled()
    {
        var ir = CompileToIr(@"
extern func abs(i32 n) -> i32
func main() -> void { i32 a = abs(5) }");

        Assert.Contains("declare i32 @abs(i32", ir);
        Assert.DoesNotContain("define i32 @abs", ir);
        Assert.Contains("call i32 @abs(i32", ir);
    }

    [Fact]
    public void ExternFunction_LowersEverySignatureType()
    {
        var ir = CompileToIr(@"
extern func strlen(string s) -> usize
extern func memset(*u8 dst, i32 value, usize count) -> *u8
extern func sqrt(f64 x) -> f64
extern func tick() -> void
func main() -> void { tick() }");

        Assert.Contains("declare i64 @strlen(ptr", ir);
        Assert.Contains("declare ptr @memset(ptr", ir);
        Assert.Contains("declare double @sqrt(double", ir);
        Assert.Contains("declare void @tick()", ir);
    }

    [Fact]
    public void ExternFunction_UnusedDeclarationStillVerifies()
    {
        var ir = CompileToIr(@"
extern func abs(i32 n) -> i32
func main() -> void { }");

        Assert.Contains("declare i32 @abs", ir);
    }

    [Fact]
    public void ExternFunction_CallsIntoTheCLibrary()
    {
        var output = Run(CompileToIr(@"
extern func abs(i32 n) -> i32
extern func strlen(string s) -> usize
extern func toupper(i32 c) -> i32
func main() -> void {
    i32 n = -7
    println(abs(n))
    println(abs(-3) + abs(4))
    println(strlen(""hello""))
    println(toupper(97))
}"));

        if (output is null) return; // lli unavailable
        Assert.Equal("7\n7\n5\n65\n", output);
    }

    [Fact]
    public void ExternFunction_CanBeWrappedByALumiFunction()
    {
        var output = Run(CompileToIr(@"
extern func abs(i32 n) -> i32
func distance(i32 a, i32 b) -> i32 { return abs(a - b) }
func main() -> void {
    println(distance(3, 10))
    println(distance(10, 3))
}"));

        if (output is null) return;
        Assert.Equal("7\n7\n", output);
    }

    [Fact]
    public void ExternStrlen_CoexistsWithTheBuiltInLen()
    {
        var output = Run(CompileToIr(@"
extern func strlen(string s) -> usize
func main() -> void {
    println(strlen(""abcd""))
    println(len(""abcd""))
}"));

        if (output is null) return;
        Assert.Equal("4\n4\n", output);
    }

    [Fact]
    public void ExternFunction_InAModule_IsCalledQualifiedAndFromWithinTheModule()
    {
        var output = Run(CompileToIr(@"
import c
func main() -> void {
    println(std::c::abs(-4))
    println(std::c::twice(-5))
}",
            ("c.lumi", @"
module std::c
extern func abs(i32 n) -> i32
func twice(i32 n) -> i32 { return abs(n) * 2 }")));

        if (output is null) return;
        Assert.Equal("4\n10\n", output);
    }

    [Fact]
    public void ExternFunction_InAModule_KeepsItsCSymbolName()
    {
        var ir = CompileToIr(@"
import c
func main() -> void { i32 a = std::c::abs(1) }",
            ("c.lumi", @"
module std::c
extern func abs(i32 n) -> i32"));

        Assert.Contains("declare i32 @abs(i32", ir);
        Assert.DoesNotContain("std_c_abs", ir);
    }

    [Fact]
    public void SameExternDeclaredInTwoModules_SharesOneDeclaration()
    {
        var ir = CompileToIr(@"
import a
import b
func main() -> void {
    println(a::twice(-1))
    println(b::thrice(-1))
}",
            ("a.lumi", @"
module a
extern func abs(i32 n) -> i32
func twice(i32 n) -> i32 { return abs(n) * 2 }"),
            ("b.lumi", @"
module b
extern func abs(i32 n) -> i32
func thrice(i32 n) -> i32 { return abs(n) * 3 }"));

        Assert.Equal(1, CountOccurrences(ir, "declare i32 @abs("));
        Assert.DoesNotContain("@abs.", ir);

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("2\n3\n", output);
    }
}
