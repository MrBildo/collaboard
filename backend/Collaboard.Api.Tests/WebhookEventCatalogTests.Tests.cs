using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

// WebhookEventCatalog + GET /api/v1/webhooks/event-types (#336) — the single server-side source of
// truth for the subscription picker. Two layers: the pure drift guard (the catalog presents exactly
// the deliver/select SoT, so the in-backend catalog can never desync from what actually fires) and
// the endpoint (full catalog grouped by family, admin-gated like the rest of the webhook surface).
public sealed class WebhookEventCatalogTests
{
    // The crux of #336: the catalog must present EXACTLY WebhookEventTypes.All. Adding an event to the
    // deliver/select SoT without display metadata here — or vice versa — fails this test, which is the
    // anti-drift binding the frontend's hand-maintained copy never had.
    [Fact]
    public void Catalog_PresentsExactly_WebhookEventTypesAll() =>
        WebhookEventCatalog.Types.ShouldBe(WebhookEventTypes.All, ignoreOrder: true);

    [Fact]
    public void Catalog_ContainsAll22Events()
    {
        var count = WebhookEventCatalog.Groups.Sum(group => group.Events.Count);

        count.ShouldBe(22);
    }

    [Fact]
    public void Catalog_Descriptors_AreWellFormed()
    {
        var descriptors = WebhookEventCatalog.Groups
            .SelectMany(group => group.Events)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            descriptor.Type.ShouldNotBeNullOrWhiteSpace();
            descriptor.Label.ShouldNotBeNullOrWhiteSpace();
            descriptor.Description.ShouldNotBeNullOrWhiteSpace();
        }

        descriptors
            .Select(descriptor => descriptor.Type)
            .Distinct(StringComparer.Ordinal)
                .Count()
                .ShouldBe(descriptors.Count, "an event type appears in more than one group");
    }

    [Fact]
    public void Catalog_Groups_AreNonEmpty_WithStableFamilyKeys()
    {
        foreach (var group in WebhookEventCatalog.Groups)
        {
            group.Family.ShouldNotBeNullOrWhiteSpace();
            group.Label.ShouldNotBeNullOrWhiteSpace();
            group.Events.ShouldNotBeEmpty();
        }

        WebhookEventCatalog.Groups
            .Select(group => group.Family)
            .ShouldBe(["card", "comment", "label", "attachment", "lane", "board"]);
    }

    [Fact]
    public async Task EventTypes_Endpoint_ReturnsFullCatalog_GroupedByFamily()
    {
        await using var factory = new CollaboardApiFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        TestAuthHelper.SetAdminAuth(client, factory);

        var response = await client.GetAsync("/api/v1/webhooks/event-types");
        response.EnsureSuccessStatusCode();

        var groups = await response.Content.ReadFromJsonAsync<JsonElement>(TestAuthHelper.JsonOptions);

        var families = groups.EnumerateArray()
            .Select(group => group.GetProperty("family").GetString())
            .ToList();
        families.ShouldBe(["card", "comment", "label", "attachment", "lane", "board"]);

        var types = groups.EnumerateArray()
            .SelectMany(group => group.GetProperty("events").EnumerateArray())
            .Select(descriptor => descriptor.GetProperty("type").GetString()!)
            .ToList();
        types.Count.ShouldBe(22);
        types.ShouldBe(WebhookEventTypes.All, ignoreOrder: true);

        // Each descriptor carries the display metadata the picker renders.
        var first = groups.EnumerateArray().First().GetProperty("events").EnumerateArray().First();
        first.GetProperty("type").GetString().ShouldBe(WebhookEventTypes.CardCreated);
        first.GetProperty("label").GetString().ShouldNotBeNullOrWhiteSpace();
        first.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(UserRole.HumanUser)]
    [InlineData(UserRole.AgentUser)]
    public async Task EventTypes_Endpoint_NonAdmin_Returns403(UserRole role)
    {
        await using var factory = new CollaboardApiFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var user = await TestAuthHelper.CreateUserAsync(client, factory, $"u-{role}", role);
        TestAuthHelper.SetAuth(client, user.AuthKey);

        (await client.GetAsync("/api/v1/webhooks/event-types")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EventTypes_Endpoint_AgentAdministrator_Succeeds()
    {
        await using var factory = new CollaboardApiFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var agentAdmin = await TestAuthHelper.CreateUserAsync(client, factory, "AgentAdmin", UserRole.AgentAdministrator);
        TestAuthHelper.SetAuth(client, agentAdmin.AuthKey);

        (await client.GetAsync("/api/v1/webhooks/event-types")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
