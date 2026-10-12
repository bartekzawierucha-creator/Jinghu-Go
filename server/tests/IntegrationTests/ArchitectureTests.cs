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
        ["JianghuGo.Modules.Inventory"] = ["JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Crafting"] = ["JianghuGo.Modules.Inventory", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Market"] = ["JianghuGo.Modules.Inventory", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Combat"] = ["JianghuGo.Modules.Inventory", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Geolocation"] = ["JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.World"] = ["JianghuGo.Modules.Geolocation", "JianghuGo.Modules.Combat", "JianghuGo.Modules.Inventory", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Cultivation"] = ["JianghuGo.Modules.Inventory", "JianghuGo.Modules.World", "JianghuGo.Modules.Combat", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
        ["JianghuGo.Modules.Notifications"] = ["JianghuGo.Modules.Market", "JianghuGo.Modules.Inventory", "JianghuGo.Modules.Combat", "JianghuGo.Modules.Cultivation", "JianghuGo.Modules.Characters", "JianghuGo.Modules.Identity", "JianghuGo.Infrastructure", "JianghuGo.Application", "JianghuGo.Domain"],
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

    /// <summary>Other modules may use only Characters.Contracts (and Cultivation's published events), never internals.</summary>
    [Fact]
    public void Cross_module_usage_goes_through_published_contracts()
    {
        foreach (var consumer in new[] { "JianghuGo.Modules.Cultivation", "JianghuGo.Modules.Notifications", "JianghuGo.Modules.Combat", "JianghuGo.Modules.World", "JianghuGo.Modules.Geolocation", "JianghuGo.Modules.Inventory", "JianghuGo.Modules.Crafting", "JianghuGo.Modules.Market" })
        {
            var asm = Assembly.Load(consumer);
            var used = asm.GetTypes()
                .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType)
                    .Concat(t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)))
                .Where(t => t.Namespace?.StartsWith("JianghuGo.Modules.", StringComparison.Ordinal) == true && !t.Namespace.StartsWith(consumer, StringComparison.Ordinal)
                            && !t.Namespace.StartsWith("JianghuGo.Modules.Identity", StringComparison.Ordinal))
                .ToList();
            foreach (var t in used)
            {
                Assert.True(t.Namespace!.EndsWith(".Contracts", StringComparison.Ordinal), $"{consumer} depends on another module's internal type {t.FullName}");
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
