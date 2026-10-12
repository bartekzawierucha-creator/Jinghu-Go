using System.Reflection;
using JianghuGo.TestKit;

namespace JianghuGo.IntegrationTests;

/// <summary>Enforces module boundaries (docs/architecture/modules.md) on compiled assemblies.</summary>
[Category("Architecture")]
public class ArchitectureTests
{
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["JianghuGo.Domain"] = [],
        ["JianghuGo.Application"] = ["JianghuGo.Domain"],
        ["JianghuGo.Infrastructure"] = ["JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Identity"] = ["JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Characters"] = ["JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
    };

    [Fact]
    public void Projects_reference_only_allowed_layers()
    {
        foreach (var (name, allowed) in Allowed)
        {
            var asm = Assembly.Load(name);
            var internalRefs = asm.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("JianghuGo.", StringComparison.Ordinal)).ToList();
            foreach (var r in internalRefs)
            {
                Assert.True(allowed.Contains(r), $"{name} must not reference {r}");
            }
        }
    }

    [Fact]
    public void Domain_has_no_framework_dependencies_beyond_the_bcl()
    {
        var refs = Assembly.Load("JianghuGo.Domain").GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.False(refs.Any(r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || r.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)),
            string.Join(", ", refs));
    }
}
