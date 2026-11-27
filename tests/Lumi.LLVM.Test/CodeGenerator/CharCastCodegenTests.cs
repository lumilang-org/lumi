using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class CharCastCodegenTests
{
    [Fact]
    public void Char_ToAndFromU8_IsAByteCopy()
    {
        var ir = CompileToIr(@"
func main() {
    char c = 'A'
    u8 b = c as u8
    char back = b as char
    println(b)
    println(back)
}");

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("65\nA\n", output);
    }

    [Fact]
    public void Char_WideningZeroExtends_SoHighBytesStayPositive()
    {
        var output = Run(CompileToIr(@"
func main() {
    char high = 200 as char
    println(high as i32)
    println(high as u32)
    println(high as i64)
    println(high as u16)
    println(high as usize)
    println(high as i8)
}"));

        if (output is null) return;
        Assert.Equal("200\n200\n200\n200\n200\n-56\n", output);
    }

    [Fact]
    public void Char_Narrowing_KeepsTheLowByte_AndNegativesWrap()
    {
        var output = Run(CompileToIr(@"
func main() {
    i32 wide = 300
    char low = wide as char
    println(low as i32)
    i32 minus = -1
    println((minus as char) as u32)
    u64 big = 65
    println(big as char)
    i16 small = 66
    println(small as char)
}"));

        if (output is null) return;
        Assert.Equal("44\n255\nA\nB\n", output);
    }

    [Fact]
    public void Char_Casts_RoundTripThroughEveryIntegerWidth()
    {
        var ir = CompileToIr(@"
func main() {
    char c = 'z'
    i8 a = c as i8
    i16 b = c as i16
    i32 d = c as i32
    i64 e = c as i64
    u8 f = c as u8
    u16 g = c as u16
    u32 h = c as u32
    u64 i = c as u64
    usize j = c as usize
    println((a as char) == c)
    println((e as char) == c)
    println((j as char) == c)
    println(b + d + (g as i16) + (f as i16))
}");

        var output = Run(ir);
        if (output is null) return;
        Assert.Equal("true\ntrue\ntrue\n488\n", output);
    }

    [Fact]
    public void StdString_WithCharCasts_Compiles_AndBuildsBytes()
    {
        // Importing std::string compiles String.from, which casts char to u8.
        var output = Run(CompileToIr(@"
import std::string
import std::mem

func main() {
    Arena arena
    arena.init(4096)
    defer arena.destroy()

    String s
    s.init(&arena, 4)
    s.push('H' as u8)
    s.push(105)
    u8[] bytes = s.as_slice()
    println(bytes.len)
    println(bytes[0] as char)
    println(bytes[1] as char)
}"));

        if (output is null) return;
        Assert.Equal("2\nH\ni\n", output);
    }

    [Fact]
    public void ByteSlice_ToString_KeepsThePointer_AndLenAndIndexingWork()
    {
        var output = Run(CompileToIr(@"
func main() {
    u8[4] buffer = [72, 105, 0, 33]
    u8[] view = buffer
    string s = view[0..3] as string
    println(s)
    println(len(s))
    println(s[1])
    println(s == ""Hi"")
    string whole = view[0..3] as string
    println(whole == s)
}"));

        if (output is null) return;
        Assert.Equal("Hi\n2\ni\ntrue\ntrue\n", output);
    }

    [Fact]
    public void EmptyByteSlice_ToString_IsTheEmptyString()
    {
        var output = Run(CompileToIr(@"
func main() {
    u8[] none
    string a = none as string
    u8[2] buffer = [1, 2]
    u8[] view = buffer
    string b = view[1..1] as string
    println(len(a))
    println(len(b))
    println(a == b)
    println(100)
}"));

        if (output is null) return;
        Assert.Equal("0\n0\ntrue\n100\n", output);
    }

    [Fact]
    public void ByteSlice_WithoutTerminatingNul_TerminatesTheProgram()
    {
        var output = Run(CompileToIr(@"
func main() {
    u8[3] buffer = [72, 105, 33]
    u8[] view = buffer
    println(1)
    string s = view as string
    println(2)
}"));

        if (output is null) return;
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Pointer_ToAndFromString_IsAReinterpretation()
    {
        var output = Run(CompileToIr(@"
func main() {
    string text = ""abc""
    *u8 raw = text as *u8
    println(raw[1] as char)
    string again = raw as string
    println(again)
    println(len(again))
}"));

        if (output is null) return;
        Assert.Equal("b\nabc\n3\n", output);
    }

    [Fact]
    public void StdString_AsString_ReturnsATerminatedCopy()
    {
        var output = Run(CompileToIr(@"
import std::string
import std::mem

func main() {
    Arena arena
    arena.init(4096)
    defer arena.destroy()

    *String owned = String.from(&arena, ""Hello"")
    owned.push_string("", Lumi"")
    string back = owned.as_string()
    println(back)
    println(len(back))
    println(back == ""Hello, Lumi"")

    String blank
    blank.init(&arena, 4)
    string nothing = blank.as_string()
    println(len(nothing))
}"));

        if (output is null) return;
        Assert.Equal("Hello, Lumi\n11\ntrue\n0\n", output);
    }
}
