namespace Lumi.Core.Semantics;

/// <summary>
/// The one place that turns module paths and names into the flat symbol names shared by the
/// type checker and codegen: std::mem + Arena -> std_mem_Arena, and Arena.reset -> std_mem_Arena__reset.
/// </summary>
public static class NameMangler
{
    public static string Mangle(string modulePrefix, string name)
    {
        return string.IsNullOrEmpty(modulePrefix) ? name : $"{Qualified(modulePrefix)}_{name}";
    }

    // std::mem::Arena -> std_mem_Arena
    public static string Qualified(string name)
    {
        return name.Replace("::", "_");
    }

    // A function declared inside a struct becomes a top-level symbol next to the struct.
    public static string Method(string mangledStructName, string methodName)
    {
        return $"{mangledStructName}__{methodName}";
    }
}
