
using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class StringCodegenTests
{
    [Fact]
    public void StringLiteral_IsPrivateConstantNulTerminatedGlobal()
    {
        var ir = CompileToIr("func main() -> void { string s = \"hi\" }");

        Assert.Contains("private unnamed_addr constant [3 x i8] c\"hi\\00\"", ir);
    }

    [Fact]
    public void StringLiteral_EscapesAreStoredAsBytes()
    {
        var ir = CompileToIr("func main() -> void { string s = \"a\\n\\t\\\"b\\\\\" }");

        Assert.Contains("c\"a\\0A\\09\\22b\\\\\\00\"", ir);
    }

    [Fact]
    public void StringLiteral_NonAsciiIsEncodedAsUtf8()
    {
        var ir = CompileToIr("func main() -> void { string s = \"é\" }");

        Assert.Contains("[3 x i8] c\"\\C3\\A9\\00\"", ir);
    }

    [Fact]
    public void IdenticalLiterals_ShareOneGlobal_DistinctOnesDoNot()
    {
        var ir = CompileToIr(
            "func main() -> void { string a = \"x\" string b = \"x\" string c = \"y\" }");

        Assert.Equal(1, CountOccurrences(ir, "c\"x\\00\""));
        Assert.Equal(1, CountOccurrences(ir, "c\"y\\00\""));
    }

    [Fact]
    public void Strings_InStructsArraysParametersAndReturns_VerifyCleanly()
    {
        var ir = CompileToIr(@"
struct Person { string name u32 age }

func pick(string a, string b, bool first) -> string {
    if (first) { return a }
    return b
}

func main() -> void {
    Person p = Person { name: ""Ada"", age: 36 }
    string[2] names = [""x"", p.name]
    string chosen = pick(names[0], names[1], false)
    names[0] = chosen
    p.name = ""Grace""
}");

        Assert.Contains("define", ir);
    }

    [Fact]
    public void Strings_ShareModule_AcrossImportedFiles_VerifyCleanly()
    {
        var ir = CompileToIr(
            @"
import util
func main() -> void { string s = util::greeting() println(s) }",
            ("util.lumi", @"
module util
func greeting() -> string { return ""hello"" }"));

        Assert.Contains("c\"hello\\00\"", ir);
    }

    [Fact]
    public void Println_String_WritesToStdout()
    {
        var output = Run(CompileToIr("func main() -> void { println(\"hello\") print(\"a\\tb\\n\") }"));

        if (output is null) return; // lli unavailable
        Assert.Equal("hello\na\tb\n", output);
    }

    [Fact]
    public void Strings_FlowThroughVariablesStructsArraysAndCalls()
    {
        var output = Run(CompileToIr(@"
struct Person { string name }
func pick(string a, string b, bool first) -> string {
    if (first) { return a }
    return b
}
func main() -> void {
    Person p = Person { name: ""Ada"" }
    string[2] names = [""x"", p.name]
    println(pick(names[0], names[1], false))
    p.name = ""Grace""
    println(p.name)
}"));

        if (output is null) return;
        Assert.Equal("Ada\nGrace\n", output);
    }

    [Fact]
    public void StdString_CanBeImportedAndCalled()
    {
        var output = Run(CompileToIr(@"
        import std::string
        func main() -> void {
            string s = std::string::identity(""std ok"")
            std::string::print_line(s)
            print(std::string::empty())
            print(std::string::newline())
        }"));

        if (output is null) return;
        Assert.Equal("std ok\n\n", output);
    }

    [Fact]
    public void StringIndexing_LowersToByteGepAndLoad()
    {
        var ir = CompileToIr("func main() -> void { string s = \"abc\" char c = s[1] }");

        Assert.Contains("getelementptr inbounds i8, ptr", ir);
        Assert.Contains("load i8, ptr %str_index_ptr", ir);
    }

    [Fact]
    public void StringIndexing_ReadsBytesIncludingEscapes()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    string s = ""a\tb""
    println(s[0])
    println(s[2])
    i32 i = 1
    if (s[i] == '\t') { println(""tab"") }
    if (s[3] == '\0') { println(""nul"") }
    if (s[0] != 'b') { println(""not b"") }
}"));

        if (output is null) return;
        Assert.Equal("a\nb\ntab\nnul\nnot b\n", output);
    }

    [Fact]
    public void Len_CallsStrlenAndReturnsI32()
    {
        var ir = CompileToIr("func main() -> void { i32 n = len(\"abc\") }");

        Assert.Contains("declare i64 @strlen(ptr", ir);
        Assert.Contains("trunc i64 %str_len to i32", ir);
        Assert.Equal(1, CountOccurrences(ir, "declare i64 @strlen"));
    }

    [Fact]
    public void Len_ComputesLengths()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    println(len(""""))
    println(len(""hello""))
    string s = ""a\nb""
    println(len(s))
}"));

        if (output is null) return;
        Assert.Equal("0\n5\n3\n", output);
    }

    [Fact]
    public void StringEquality_UsesStrcmpDeclaredOnce()
    {
        var ir = CompileToIr(@"
func main() -> void {
    bool a = ""x"" == ""y""
    bool b = ""x"" != ""y""
}");

        Assert.Equal(1, CountOccurrences(ir, "declare i32 @strcmp"));
        Assert.Contains("icmp eq i32 %str_cmp", ir);
        Assert.Contains("icmp ne i32 %str_cmp", ir);
    }

    [Fact]
    public void StringEquality_ComparesContents()
    {
        var output = Run(CompileToIr(@"
func same(string a, string b) -> bool { return a == b }
func main() -> void {
    string x = ""abc""
    println(same(x, ""abc""))
    println(same(x, ""abd""))
    println(same("""", """"))
    println(x != ""abc"")
    println(x != ""zzz"")
}"));

        if (output is null) return;
        Assert.Equal("true\nfalse\ntrue\nfalse\ntrue\n", output);
    }

    [Fact]
    public void StdString_NewHelpers_Work()
    {
        var output = Run(CompileToIr(@"
import std::string
func main() -> void {
    println(std::string::is_empty(""""))
    println(std::string::is_empty(""x""))
    println(std::string::equals(""ab"", ""ab""))
    println(std::string::equals(""ab"", ""ba""))
    println(std::string::count_char(""banana"", 'a'))
    println(std::string::count_char(""banana"", 'z'))
    println(std::string::index_of(""banana"", 'n'))
    println(std::string::index_of(""banana"", 'z'))
    println(std::string::starts_with_char(""banana"", 'b'))
    println(std::string::starts_with_char(""banana"", 'a'))
    println(std::string::starts_with_char("""", 'a'))
    println(std::string::ends_with_char(""banana"", 'a'))
    println(std::string::ends_with_char(""banana"", 'b'))
    println(std::string::ends_with_char("""", 'a'))
}"));

        if (output is null) return;
        Assert.Equal(
            "true\nfalse\ntrue\nfalse\n3\n0\n2\n-1\ntrue\nfalse\nfalse\ntrue\nfalse\nfalse\n",
            output);
    }
}
