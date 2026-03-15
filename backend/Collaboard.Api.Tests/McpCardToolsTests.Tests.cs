using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Collaboard.Api.Tests;

public class McpCardToolsTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private BoardDbContext _db = null!;
    private CardTools _cardTools = null!;
    private string _authKey = null!;
    private Guid _boardId;
    private Guid _laneId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BoardDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new BoardDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // Seed test data
        _authKey = "test-mcp-auth-key-12345678";
        var user = new BoardUser
        {
            Id = Guid.NewGuid(),
            AuthKey = _authKey,
            Name = "TestAdmin",
            Role = UserRole.Administrator,
            IsActive = true,
        };
        _db.Users.Add(user);

        _boardId = Guid.NewGuid();
        _db.Boards.Add(new Board
        {
            Id = _boardId,
            Name = "Test Board",
            Slug = "test-board",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        _laneId = Guid.NewGuid();
        _db.Lanes.Add(new Lane
        {
            Id = _laneId,
            BoardId = _boardId,
            Name = "To Do",
            Position = 0,
        });

        await _db.SaveChangesAsync();

        var authService = new McpAuthService(_db);
        var broadcaster = new BoardEventBroadcaster();
        _cardTools = new CardTools(_db, authService, broadcaster);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Guid> CreateLabelAsync(string name, string? color = null, Guid? boardId = null)
    {
        var label = new Label
        {
            Id = Guid.NewGuid(),
            BoardId = boardId ?? _boardId,
            Name = name,
            Color = color,
        };
        _db.Labels.Add(label);
        await _db.SaveChangesAsync();
        return label.Id;
    }

    [Fact]
    public async Task CreateCard_WithLabelIds_AssignsLabels()
    {
        // Arrange
        var label1Id = await CreateLabelAsync("bug", "red");
        var label2Id = await CreateLabelAsync("feature", "green");
        var labelIds = $"{label1Id},{label2Id}";

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Card With Labels", _laneId, labelIds: labelIds);

        // Assert
        result.ShouldNotContain("Error");
        var card = JsonSerializer.Deserialize<JsonElement>(result);
        var cardId = card.GetProperty("id").GetGuid();

        var cardLabels = await _db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync();
        cardLabels.Count.ShouldBe(2);
        cardLabels.ShouldContain(cl => cl.LabelId == label1Id);
        cardLabels.ShouldContain(cl => cl.LabelId == label2Id);
    }

    [Fact]
    public async Task CreateCard_WithSingleLabelId_AssignsLabel()
    {
        // Arrange
        var labelId = await CreateLabelAsync("improvement", "blue");

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Single Label Card", _laneId, labelIds: labelId.ToString());

        // Assert
        result.ShouldNotContain("Error");
        var card = JsonSerializer.Deserialize<JsonElement>(result);
        var cardId = card.GetProperty("id").GetGuid();

        var cardLabels = await _db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync();
        cardLabels.Count.ShouldBe(1);
        cardLabels[0].LabelId.ShouldBe(labelId);
    }

    [Fact]
    public async Task CreateCard_WithNoLabelIds_CreatesCardWithoutLabels()
    {
        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "No Labels Card", _laneId);

        // Assert
        result.ShouldNotContain("Error");
        var card = JsonSerializer.Deserialize<JsonElement>(result);
        var cardId = card.GetProperty("id").GetGuid();

        var cardLabels = await _db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync();
        cardLabels.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_WithNonexistentLabel_ReturnsError()
    {
        // Arrange
        var bogusLabelId = Guid.NewGuid();

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Bad Label Card", _laneId, labelIds: bogusLabelId.ToString());

        // Assert
        result.ShouldContain("Error");
        result.ShouldContain(bogusLabelId.ToString());
        result.ShouldContain("not found");

        // Card should not have been created
        var cards = await _db.Cards.Where(c => c.Name == "Bad Label Card").ToListAsync();
        cards.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_WithCrossBoardLabel_ReturnsError()
    {
        // Arrange — create a label on a different board
        var otherBoardId = Guid.NewGuid();
        _db.Boards.Add(new Board
        {
            Id = otherBoardId,
            Name = "Other Board",
            Slug = "other-board",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var crossBoardLabelId = await CreateLabelAsync("cross-board-label", "red", otherBoardId);

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Cross Board Label Card", _laneId, labelIds: crossBoardLabelId.ToString());

        // Assert
        result.ShouldContain("Error");
        result.ShouldContain(crossBoardLabelId.ToString());
        result.ShouldContain("does not belong to the same board");

        // Card should not have been created
        var cards = await _db.Cards.Where(c => c.Name == "Cross Board Label Card").ToListAsync();
        cards.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_WithInvalidLabelIdFormat_ReturnsError()
    {
        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Invalid Format Card", _laneId, labelIds: "not-a-guid");

        // Assert
        result.ShouldContain("Error");
        result.ShouldContain("Invalid label ID format");

        // Card should not have been created
        var cards = await _db.Cards.Where(c => c.Name == "Invalid Format Card").ToListAsync();
        cards.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_WithMixedValidAndInvalidLabels_ReturnsError()
    {
        // Arrange — one valid label, one nonexistent
        var validLabelId = await CreateLabelAsync("valid-label", "blue");
        var bogusLabelId = Guid.NewGuid();
        var labelIds = $"{validLabelId},{bogusLabelId}";

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Mixed Labels Card", _laneId, labelIds: labelIds);

        // Assert
        result.ShouldContain("Error");
        result.ShouldContain(bogusLabelId.ToString());

        // Card should not have been created
        var cards = await _db.Cards.Where(c => c.Name == "Mixed Labels Card").ToListAsync();
        cards.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_WithLabelsAndSpaces_ParsesCorrectly()
    {
        // Arrange
        var label1Id = await CreateLabelAsync("spaced-label-1", "red");
        var label2Id = await CreateLabelAsync("spaced-label-2", "green");
        var labelIds = $" {label1Id} , {label2Id} ";

        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Spaced Labels Card", _laneId, labelIds: labelIds);

        // Assert
        result.ShouldNotContain("Error");
        var card = JsonSerializer.Deserialize<JsonElement>(result);
        var cardId = card.GetProperty("id").GetGuid();

        var cardLabels = await _db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync();
        cardLabels.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CreateCard_WithEmptyLabelIds_CreatesCardWithoutLabels()
    {
        // Act
        var result = await _cardTools.CreateCardAsync(_authKey, "Empty LabelIds Card", _laneId, labelIds: "");

        // Assert
        result.ShouldNotContain("Error");
        var card = JsonSerializer.Deserialize<JsonElement>(result);
        var cardId = card.GetProperty("id").GetGuid();

        var cardLabels = await _db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync();
        cardLabels.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCard_LabelsVisibleViaGetCard()
    {
        // Arrange
        var labelId = await CreateLabelAsync("visible-label", "purple");

        // Act
        var createResult = await _cardTools.CreateCardAsync(_authKey, "Visible Labels Card", _laneId, labelIds: labelId.ToString());
        createResult.ShouldNotContain("Error");
        var createdCard = JsonSerializer.Deserialize<JsonElement>(createResult);
        var cardId = createdCard.GetProperty("id").GetGuid();

        var getResult = await _cardTools.GetCardAsync(_authKey, cardId);

        // Assert
        var cardData = JsonSerializer.Deserialize<JsonElement>(getResult);
        var labels = cardData.GetProperty("labels");
        labels.GetArrayLength().ShouldBe(1);
        labels[0].GetProperty("id").GetGuid().ShouldBe(labelId);
        labels[0].GetProperty("name").GetString().ShouldBe("visible-label");
    }
}
