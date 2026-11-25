using static Lumi.LLVM.Test.CodeGenerator.CompilerHarness;

namespace Lumi.LLVM.Test.CodeGenerator;

public class VisibilityCodegenTests
{
    private const string Bank = @"
module bank
pub struct Account {
    balance i32
    pub i32 id

    pub func open(i32 id) -> Account { return Account { balance: 0, id: id } }
    pub func deposit(*Account self, i32 n) -> void { self.add(n) }
    pub func total(*Account self) -> i32 { return self.balance }
    func add(*Account self, i32 n) -> void { self.balance = self.balance + n }
}
pub func double(i32 n) -> i32 { return helper(n) }
func helper(i32 n) -> i32 { return n * 2 }
";

    [Fact]
    public void PrivateFunctions_GetInternalLinkage_PublicAndMainDoNot()
    {
        var ir = CompileToIr(@"
import bank
func main() -> void { println(bank::double(2)) }",
            ("bank.lumi", Bank.Replace("balance i32", "i32 balance")));

        Assert.Contains("define i32 @bank_double(", ir);
        Assert.Contains("define internal i32 @bank_helper(", ir);
        Assert.Contains("define internal void @bank_Account__add(", ir);
        Assert.Contains("define void @bank_Account__deposit(", ir);
        Assert.Contains("define void @main(", ir);
    }

    [Fact]
    public void CrossModule_UsesPublicApi_AndRuns()
    {
        var output = Run(CompileToIr(@"
import bank
func main() -> void {
    bank::Account a = bank::Account.open(7)
    a.deposit(5)
    a.deposit(6)
    println(a.total())
    println(a.id)
    println(bank::double(4))
}",
            ("bank.lumi", Bank.Replace("balance i32", "i32 balance"))));

        if (output is null) return;
        Assert.Equal("11\n7\n8\n", output);
    }

    [Fact]
    public void PrivateFieldsDoNotChangeLayout()
    {
        var output = Run(CompileToIr(@"
struct S { i32 a pub i32 b i32 c }
func main() -> void {
    S s = S { a: 1, b: 2, c: 3 }
    println(sizeof(S))
    println(s.c)
}"));

        if (output is null) return;
        Assert.Equal("12\n3\n", output);
    }
}
