using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class TupleCodegenTests
{
    [Fact]
    public void Tuple_FunctionReturn_AndDestructuring()
    {
        var output = Run(CompileToIr(@"
func divmod(i32 a, i32 b) -> (i32, i32) {
    return (a / b, a - (a / b) * b)
}
func main() -> void {
    let (q, r) = divmod(17, 5)
    println(q)
    println(r)
}"));

        if (output is null) return;
        Assert.Equal("3\n2\n", output);
    }

    [Fact]
    public void Tuple_WildcardsNestedTuplesAndMixedTypes()
    {
        var output = Run(CompileToIr(@"
func make() -> (i32, (bool, i64), char) {
    return (1, (true, 40), 'z')
}
func main() -> void {
    let (a, (flag, big), c) = make()
    let (_, (_, n), _) = make()
    println(a)
    println(flag)
    println(big + n)
    println(c)
}"));

        if (output is null) return;
        Assert.Equal("1\ntrue\n80\nz\n", output);
    }

    [Fact]
    public void Tuple_VariablesParametersFieldsAndInference()
    {
        var output = Run(CompileToIr(@"
struct Box { (i32, i32) pair i32 tag }
func sum((i32, i32) p) -> i32 {
    let (x, y) = p
    return x + y
}
func main() -> void {
    (i32, i32) a = (3, 4)
    let b = (10, 20)
    Box box = Box { pair: (5, 6), tag: 1 }
    (i64, u8) wide = (7, 8)
    println(sum(a))
    println(sum(b))
    println(sum(box.pair))
    println(sum((1, 1)))
    let (w, v) = wide
    println(w)
    println(v)
}"));

        if (output is null) return;
        Assert.Equal("7\n30\n11\n2\n7\n8\n", output);
    }

    [Fact]
    public void Tuple_SizeofAlignofMatchAggregateLayout()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    println(sizeof((i32, i32)))
    println(sizeof((u8, i64)))
    println(alignof((u8, i64)))
    println(sizeof((i32, i32)[3]))
}"));

        if (output is null) return;
        Assert.Equal("8\n16\n8\n24\n", output);
    }

    [Fact]
    public void Tuple_ModuleVerifies_AndLowersToAnonymousStruct()
    {
        var ir = CompileToIr(@"
func pair(i32 x) -> (i32, bool) { return (x, true) }
func main() -> void { let (n, b) = pair(1) }");

        Assert.Contains("{ i32, i1 }", ir);
        Assert.Contains("insertvalue", ir);
        Assert.Contains("extractvalue", ir);
    }

    [Fact]
    public void Tuple_PointerToTuple_AndArrayOfTuples()
    {
        var output = Run(CompileToIr(@"
func main() -> void {
    (i32, i32)[2] items = [(1, 2), (3, 4)]
    (i32, i32) t = (9, 9)
    *(i32, i32) p = &t
    let (a, b) = *p
    let (c, d) = items[1]
    println(a + b)
    println(c * d)
}"));

        if (output is null) return;
        Assert.Equal("18\n12\n", output);
    }

    [Fact]
    public void Tuple_ElementAccess_ReadsAndWrites()
    {
        var output = Run(CompileToIr(@"
struct Box { (i32, (bool, i64)) data }
func pair(i32 n) -> (i32, i32) { return (n, n * 2) }
func main() -> void {
    (i32, i32) t = (1, 2)
    t.0 = 10
    t.1 = t.0 + t.1
    println(t.0)
    println(t.1)

    let nested = (1, (true, 40))
    println(nested.1.0)
    println(nested.1.1)

    Box box = Box { data: (5, (false, 6)) }
    box.data.1.1 = (box.data.0 + 100) as i64
    println(box.data.1.1)

    println(pair(7).1)

    *(i32, i32) p = &t
    p.0 = 99
    println(p.0)
    println(t.0)

    (i32, i32)[2] items = [(1, 2), (3, 4)]
    items[1].0 = 30
    println(items[1].0 + items[0].1)
    t.0++
    println(t.0)
}"));

        if (output is null) return;
        Assert.Equal("10\n12\ntrue\n40\n105\n14\n99\n99\n32\n100\n", output);
    }

    [Fact]
    public void Function_WithoutArrow_IsVoid_AndRuns()
    {
        var output = Run(CompileToIr(@"
struct Counter {
    i32 n
    func bump(*Counter self) { self.n = self.n + 1 }
}
func show(i32 v) { println(v) }
func main() {
    Counter c = Counter { n: 1 }
    c.bump()
    show(c.n)
}"));

        if (output is null) return;
        Assert.Equal("2\n", output);
    }
}
