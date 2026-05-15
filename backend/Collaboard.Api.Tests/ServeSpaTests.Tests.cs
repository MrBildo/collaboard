using System.Net;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class ServeSpaTests
{
    private static async Task<CollaboardApiFactory> CreateFactoryAsync(
        IReadOnlyDictionary<string, string?> overrides)
    {
        var factory = CollaboardApiFactory.WithConfig(overrides);
        await factory.InitializeAsync();
        return factory;
    }

    // Default ServeSpa=true is not positively assertable here: no SPA bundle is built
    // into wwwroot in the test host, so MapFallbackToFile has no index.html to serve and
    // returns 404 — indistinguishable from the route-absent 404. Spec §3.9.2 explicitly
    // accepts parity-with-today: the full existing suite (all green under default
    // ServeSpa=true) is the default-behavior-preserved guarantee (acceptance #1). These
    // tests pin the deterministic half — the ServeSpa=false headless contract.

    [Fact]
    public async Task ServeSpa_False_RootReturns404()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServeSpa_False_DeepLinkReturns404()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/boards/test-slug");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServeSpa_False_ApiStillServes()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServeSpa_False_McpStillServes()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.PostAsync("/mcp", content: null);

        // The MCP transport rejects a malformed POST, but it must NOT 404 — the route
        // is mapped on `app`, independent of the SPA-fallback middleware.
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServeSpa_False_HealthStillServes()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServeSpa_False_AliveStillServes()
    {
        await using var factory = await CreateFactoryAsync(new Dictionary<string, string?>
        {
            ["Hosting:ServeSpa"] = "false",
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("/alive");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
