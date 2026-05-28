using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

// Role-gate matrix coverage for the AgentAdministrator role (card #243, Phase 1).
//
// Spec: .agents/specs/agent-admin-mcp.md — Part 1, "Per-endpoint disposition (REST)".
//
// Each widened endpoint is exercised with three role classes:
//   - Administrator         → expected success (status quo)
//   - AgentAdministrator    → expected success (the new admit)
//   - HumanUser / AgentUser → expected 403 (status quo)
//
// Strict endpoints (DELETE /boards/{id}, user CRUD, prune action=delete) are
// also verified to keep rejecting AgentAdministrator.
public class AgentAdminRoleGateTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        TestAuthHelper.SetAdminAuth(client, _factory);
        return client;
    }

    private async Task<HttpClient> ClientForRoleAsync(UserRole role, string nameHint)
    {
        var setupClient = _factory.CreateClient();
        var user = await TestAuthHelper.CreateUserAsync(
            setupClient,
            _factory,
            $"{nameHint}-{role}-{Guid.NewGuid():N}",
            role);

        var client = _factory.CreateClient();
        TestAuthHelper.SetAuth(client, user.AuthKey);
        return client;
    }

    // ---------------------------------------------------------------------
    // Lanes — POST, PATCH, DELETE all widen to AdminOrAgentAdmin.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.Created)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PostLane_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "post-lane");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/lanes",
            new { name = $"RoleGate Lane {Guid.NewGuid():N}", position = Random.Shared.Next(50_000, 100_000) });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PatchLane_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var laneId = await CreateLaneAsAdminAsync("RoleGate Patch Lane", Random.Shared.Next(50_000, 100_000));
        var client = await ClientForRoleAsync(role, "patch-lane");

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/lanes/{laneId}",
            new { name = "Renamed" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task DeleteLane_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var laneId = await CreateLaneAsAdminAsync("RoleGate Delete Lane", Random.Shared.Next(50_000, 100_000));
        var client = await ClientForRoleAsync(role, "delete-lane");

        // Act
        var response = await client.DeleteAsync($"/api/v1/lanes/{laneId}");

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------
    // Labels — POST, PATCH, DELETE all widen to AdminOrAgentAdmin.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.Created)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PostLabel_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "post-label");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"RoleGate Label {Guid.NewGuid():N}", color = "#abcdef" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PatchLabel_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var labelId = await CreateLabelAsAdminAsync();
        var client = await ClientForRoleAsync(role, "patch-label");

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/labels/{labelId}",
            new { name = $"Renamed-{role}-{Guid.NewGuid():N}" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task DeleteLabel_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var labelId = await CreateLabelAsAdminAsync();
        var client = await ClientForRoleAsync(role, "delete-label");

        // Act
        var response = await client.DeleteAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels/{labelId}");

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------
    // Sizes — POST, PATCH, DELETE all widen to AdminOrAgentAdmin.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.Created)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PostSize_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "post-size");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/sizes",
            new { name = $"RG-{Guid.NewGuid():N}".Substring(0, 8) });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PatchSize_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var sizeId = await CreateSizeAsAdminAsync();
        var client = await ClientForRoleAsync(role, "patch-size");

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/sizes/{sizeId}",
            new { name = $"R-{role}-{Guid.NewGuid().ToString("N").Substring(0, 6)}" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.NoContent)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task DeleteSize_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var sizeId = await CreateSizeAsAdminAsync();
        var client = await ClientForRoleAsync(role, "delete-size");

        // Act
        var response = await client.DeleteAsync($"/api/v1/sizes/{sizeId}");

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    // ---------------------------------------------------------------------
    // Boards — POST and PATCH widen; DELETE stays strict.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.Created)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.Created)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PostBoard_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "post-board");

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/v1/boards",
            new { name = $"RoleGate Board {Guid.NewGuid():N}" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PatchBoard_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var boardId = await CreateBoardAsAdminAsync();
        var client = await ClientForRoleAsync(role, "patch-board");

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/boards/{boardId}",
            new { name = $"Renamed {Guid.NewGuid():N}" });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    // DELETE /boards/{id} stays strict — AgentAdministrator must be 403, not success.
    [Theory]
    [InlineData(UserRole.AgentAdministrator)]
    [InlineData(UserRole.HumanUser)]
    [InlineData(UserRole.AgentUser)]
    public async Task DeleteBoard_NonStrictAdmin_Returns403(UserRole role)
    {
        // Arrange
        var boardId = await CreateBoardAsAdminAsync();
        var client = await ClientForRoleAsync(role, "delete-board");

        // Act
        var response = await client.DeleteAsync($"/api/v1/boards/{boardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------------
    // Prune — preview + action=archive widen; action=delete stays Administrator-only.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PrunePreview_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "prune-preview");

        // Act — pass a filter (preview requires at least one)
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/prune/preview",
            new { olderThan = DateTimeOffset.UtcNow.AddYears(-100) });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Administrator, HttpStatusCode.OK)]
    [InlineData(UserRole.AgentAdministrator, HttpStatusCode.OK)]
    [InlineData(UserRole.HumanUser, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.AgentUser, HttpStatusCode.Forbidden)]
    public async Task PruneArchive_RoleGate(UserRole role, HttpStatusCode expected)
    {
        // Arrange
        var client = await ClientForRoleAsync(role, "prune-archive");

        // Act — archive action, filter that matches nothing (no cards old enough)
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/prune",
            new { action = "archive", olderThan = DateTimeOffset.UtcNow.AddYears(-100) });

        // Assert
        response.StatusCode.ShouldBe(expected);
    }

    // action=delete is rejected in-body for AgentAdministrator (the only
    // in-body role check the design admits — see spec Part 1 and PruneEndpoints.cs).
    [Fact]
    public async Task PruneDelete_AsAgentAdministrator_Returns403()
    {
        // Arrange
        var client = await ClientForRoleAsync(UserRole.AgentAdministrator, "prune-delete");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/prune",
            new { action = "delete", olderThan = DateTimeOffset.UtcNow.AddYears(-100) });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PruneDelete_AsAdministrator_Returns200()
    {
        // Arrange — strict admin can still delete via prune
        var client = AdminClient();

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/prune",
            new { action = "delete", olderThan = DateTimeOffset.UtcNow.AddYears(-100) });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------
    // Users — all admin user endpoints stay strict; AgentAdministrator is 403.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PostUser_AsAgentAdministrator_Returns403()
    {
        // Arrange
        var client = await ClientForRoleAsync(UserRole.AgentAdministrator, "post-user");

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { name = "Should Not Create", role = (int)UserRole.AgentUser });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchUser_AsAgentAdministrator_Returns403()
    {
        // Arrange — make a target user; only an Administrator should be able to PATCH them.
        var adminClient = AdminClient();
        var targetResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/users",
            new { name = $"PatchTarget-{Guid.NewGuid():N}", role = (int)UserRole.AgentUser });
        targetResponse.EnsureSuccessStatusCode();
        var target = await targetResponse.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetGuid();

        var client = await ClientForRoleAsync(UserRole.AgentAdministrator, "patch-user");

        // Act
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/users/{targetId}",
            new { name = "Renamed" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivateUser_AsAgentAdministrator_Returns403()
    {
        // Arrange
        var adminClient = AdminClient();
        var targetResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/users",
            new { name = $"DeactivateTarget-{Guid.NewGuid():N}", role = (int)UserRole.AgentUser });
        targetResponse.EnsureSuccessStatusCode();
        var target = await targetResponse.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetGuid();

        var client = await ClientForRoleAsync(UserRole.AgentAdministrator, "deactivate-user");

        // Act
        var response = await client.PatchAsync($"/api/v1/users/{targetId}/deactivate", content: null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_AsAgentAdministrator_Returns403()
    {
        // Arrange
        var client = await ClientForRoleAsync(UserRole.AgentAdministrator, "get-users");

        // Act
        var response = await client.GetAsync("/api/v1/users");

        // Assert — auth-key list is sensitive; AgentAdministrator must not see it.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<Guid> CreateLaneAsAdminAsync(string namePrefix, int position)
    {
        var client = AdminClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/lanes",
            new { name = $"{namePrefix} {Guid.NewGuid():N}", position });
        response.EnsureSuccessStatusCode();
        var lane = await response.Content.ReadFromJsonAsync<JsonElement>();
        return lane.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateLabelAsAdminAsync()
    {
        var client = AdminClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"RoleGate Label {Guid.NewGuid():N}", color = "#cccccc" });
        response.EnsureSuccessStatusCode();
        var label = await response.Content.ReadFromJsonAsync<JsonElement>();
        return label.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateSizeAsAdminAsync()
    {
        var client = AdminClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/sizes",
            new { name = $"RG-{Guid.NewGuid().ToString("N").Substring(0, 6)}" });
        response.EnsureSuccessStatusCode();
        var size = await response.Content.ReadFromJsonAsync<JsonElement>();
        return size.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBoardAsAdminAsync()
    {
        var client = AdminClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/boards",
            new { name = $"RoleGate Board {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("id").GetGuid();
    }
}
