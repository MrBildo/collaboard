using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class AttachmentEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;
    private static int _nextPosition = 3000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AttachmentEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> CreateCardAsync()
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var laneId = board.GetProperty("lanes")[0].GetProperty("id").GetGuid();

        var cardPayload = new
        {
            name = "Test Card",
            descriptionMarkdown = "Card for attachment tests",
            laneId,
            position = Interlocked.Increment(ref _nextPosition),
        };

        var cardResponse = await _client.PostAsJsonAsync("/api/v1/cards", cardPayload);
        cardResponse.EnsureSuccessStatusCode();
        var card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return card.GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent CreateFileUpload(byte[]? data = null, string fileName = "test.bin")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(data ?? [1, 2, 3, 4]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private async Task<Guid> UploadAttachmentAsync(Guid cardId)
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var upload = CreateFileUpload();
        var response = await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", upload);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task PostAttachment_OnExistingCard_Returns201()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var upload = CreateFileUpload(fileName: "document.bin");

        // Act
        var response = await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", upload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.TryGetProperty("id", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());
        Assert.Equal("document.bin", json.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task PostAttachment_OnNonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fakeCardId = Guid.NewGuid();
        var upload = CreateFileUpload();

        // Act
        var response = await _client.PostAsync($"/api/v1/cards/{fakeCardId}/attachments", upload);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAttachment_ReturnsFileWithCorrectContent()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fileBytes = new byte[] { 10, 20, 30, 40, 50 };
        var upload = CreateFileUpload(fileBytes, "payload.bin");

        var uploadResponse = await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", upload);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadJson = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var attachmentId = uploadJson.GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/attachments/{attachmentId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileBytes, downloadedBytes);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Contains("payload.bin", response.Content.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task GetAttachment_NonexistentAttachment_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fakeId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/attachments/{fakeId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAttachment_AsAdmin_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var attachmentId = await UploadAttachmentAsync(cardId);
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAttachment_AsHumanUser_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var attachmentId = await UploadAttachmentAsync(cardId);
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Human Deleter", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAttachment_AsAgentUser_Returns403()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var attachmentId = await UploadAttachmentAsync(cardId);
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "Agent Forbidden Delete", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, agent.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/attachments/{attachmentId}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAttachment_NonexistentAttachment_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fakeId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/attachments/{fakeId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
