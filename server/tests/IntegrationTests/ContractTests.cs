using System.Text.RegularExpressions;
using JianghuGo.TestKit;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JianghuGo.IntegrationTests;

[Category("Contract")]
public partial class ContractTests : IAsyncLifetime
{
    private ApiHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiHost.StartAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [GeneratedRegex(@"^  (/\S*):\s*$")]
    private static partial Regex PathLine();

    [GeneratedRegex(@"^    (get|post|put|patch|delete):\s*$")]
    private static partial Regex MethodLine();

    [GeneratedRegex(@"\{(\w+)(:[^}]*)?\}")]
    private static partial Regex RouteParam();

    [Fact]
    public void OpenApi_document_matches_implemented_routes_exactly()
    {
        var documented = new HashSet<string>();
        string? currentPath = null;
        var inPaths = false;
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "contracts", "jianghu-go.v1.yaml")))
        {
            if (line == "paths:") { inPaths = true; continue; }
            if (inPaths && line.Length > 0 && !char.IsWhiteSpace(line[0])) break;
            if (!inPaths) continue;
            if (PathLine().Match(line) is { Success: true } p) currentPath = p.Groups[1].Value;
            else if (MethodLine().Match(line) is { Success: true } m && currentPath is not null)
                documented.Add($"{m.Groups[1].Value.ToUpperInvariant()} {currentPath}");
        }

        var implemented = _host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} /{RouteParam().Replace(e.RoutePattern.RawText!.TrimStart('/'), "{$1}").TrimEnd('/')}"))
            .ToHashSet();

        var undocumented = implemented.Except(documented).Order().ToList();
        var phantom = documented.Except(implemented).Order().ToList();
        Assert.True(undocumented.Count == 0, "Implemented but not in OpenAPI: " + string.Join(", ", undocumented));
        Assert.True(phantom.Count == 0, "In OpenAPI but not implemented: " + string.Join(", ", phantom));
    }
}
