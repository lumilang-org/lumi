using Lumi.Core.Diagnostics;
using Lumi.Core.Semantics;

namespace Lumi.Core.Test.Semantics;

[TestFixture]
public class ModuleLoaderTests
{
    private string _tempDir;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Load_SimpleDependency()
    {
        var mathPath = Path.Combine(_tempDir, "math.lumi");
        File.WriteAllText(mathPath, "module math\nfunc add(i32 a, i32 b) -> i32 { return a + b }");

        var mainPath = Path.Combine(_tempDir, "main.lumi");
        File.WriteAllText(mainPath, "import math\nfunc main() -> void { i32 res = add(1, 2) }");

        var diagnostics = new DiagnosticBag();
        var loader = new ModuleLoader(diagnostics);
        var modules = loader.Load(mainPath);

        Assert.That(diagnostics.HasErrors, Is.False);
        Assert.That(modules, Has.Count.EqualTo(2));
        Assert.That(modules[0].Module.PathSegments[0], Is.EqualTo("math"));
        Assert.That(modules[1].Imports[0].PathSegments[0], Is.EqualTo("math"));
    }

    [Test]
    public void Load_CircularDependency_ReportsError()
    {
        var aPath = Path.Combine(_tempDir, "a.lumi");
        var bPath = Path.Combine(_tempDir, "b.lumi");

        File.WriteAllText(aPath, "import b\nfunc a() -> void {}");
        File.WriteAllText(bPath, "import a\nfunc b() -> void {}");

        var diagnostics = new DiagnosticBag();
        var loader = new ModuleLoader(diagnostics);
        var modules = loader.Load(aPath);

        Assert.That(diagnostics.HasErrors, Is.True);
        Assert.That(diagnostics.Diagnostics.Any(d => d.Code == DiagnosticCodes.CircularImport), Is.True);
    }
}
