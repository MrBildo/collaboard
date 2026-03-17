using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

public class McpCardToolsTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>, IDisposable
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly List<IServiceScope> _scopes = [];

    private (BoardDbContext Db, CardTools Tools, string AuthKey) CreateTools()
    {
        var scope = _factory.Services.CreateScope();
        _scopes.Add(scope);
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
        var auth = new McpAuthService(db);
        var tools = new CardTools(db, auth, broadcaster);
        return (db, tools, CollaboardApiFactory.TestAdminAuthKey);
    }

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }
    }

    private async Task<(Guid LaneId1, Guid LaneId2)> GetTwoLaneIdsAsync(BoardDbContext db)
    {
        var lanes = db.Lanes
            .Where(l => l.BoardId == _factory.DefaultBoardId)
            .OrderBy(l => l.Position)
            .Select(l => l.Id)
            .ToList();

        lanes.Count.ShouldBeGreaterThanOrEqualTo(2);
        return (lanes[0], lanes[1]);
    }

    private async Task<Guid> CreateCardInLaneAsync(CardTools tools, string authKey, Guid laneId, string name = "Test Card")
    {
        var result = await tools.CreateCardAsync(authKey, name, laneId);
        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    // ── No-op guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCard_NoChangesSpecified_ReturnsNoOpMessage()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId);

        // Assert
        result.ShouldBe("No changes specified.");
    }

    [Fact]
    public async Task UpdateCard_NoChangesSpecified_DoesNotUpdateTimestamp()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);
        var card = await db.Cards.FindAsync(cardId);
        var originalTimestamp = card!.LastUpdatedAtUtc;

        await Task.Delay(50);

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId);

        // Assert
        result.ShouldBe("No changes specified.");
        var refreshedCard = await db.Cards.FindAsync(cardId);
        refreshedCard!.LastUpdatedAtUtc.ShouldBe(originalTimestamp);
    }

    // ── Lane move via update_card ───────────────────────────────────────────

    [Fact]
    public async Task UpdateCard_WithLaneId_MovesCardToTargetLane()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);
        var cardId = await CreateCardInLaneAsync(tools, authKey, lane1, "Move Me");

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, laneId: lane2);

        // Assert
        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        json.RootElement.GetProperty("laneId").GetGuid().ShouldBe(lane2);
    }

    [Fact]
    public async Task UpdateCard_WithLaneIdAndIndex_PlacesCardAtSpecifiedPosition()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        // Create two existing cards in lane2
        await CreateCardInLaneAsync(tools, authKey, lane2, "Existing Card A");
        await CreateCardInLaneAsync(tools, authKey, lane2, "Existing Card B");

        // Create the card to move in lane1
        var cardId = await CreateCardInLaneAsync(tools, authKey, lane1, "Insert Me");

        // Act — move to lane2 at index 0 (front)
        var result = await tools.UpdateCardAsync(authKey, cardId, laneId: lane2, index: 0);

        // Assert
        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        json.RootElement.GetProperty("laneId").GetGuid().ShouldBe(lane2);
        json.RootElement.GetProperty("position").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task UpdateCard_WithLaneIdNoIndex_AppendsToEndOfLane()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        // Create an existing card in lane2
        await CreateCardInLaneAsync(tools, authKey, lane2, "Already Here");

        // Create the card to move
        var cardId = await CreateCardInLaneAsync(tools, authKey, lane1, "Append Me");

        // Act — move to lane2 without index (should append)
        var result = await tools.UpdateCardAsync(authKey, cardId, laneId: lane2);

        // Assert
        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        json.RootElement.GetProperty("laneId").GetGuid().ShouldBe(lane2);

        // Card should have the highest position in lane2
        var maxPosOtherCards = db.Cards
            .Where(c => c.LaneId == lane2 && c.Id != cardId)
            .Max(c => (int?)c.Position) ?? -1;
        json.RootElement.GetProperty("position").GetInt32().ShouldBeGreaterThan(maxPosOtherCards);
    }

    [Fact]
    public async Task UpdateCard_WithInvalidLaneId_ReturnsError()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, laneId: Guid.NewGuid());

        // Assert
        result.ShouldContain("Error: Lane not found.");
    }

    [Fact]
    public async Task UpdateCard_WithLaneId_ReordersSourceLane()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        // Create three cards in lane1
        var card1Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Source A");
        var card2Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Source B");
        var card3Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Source C");

        // Act — move the middle card to lane2
        await tools.UpdateCardAsync(authKey, card2Id, laneId: lane2);

        // Assert — card2 is no longer in lane1
        var card2 = await db.Cards.FindAsync(card2Id);
        card2!.LaneId.ShouldBe(lane2);

        // Assert — remaining source cards maintain gap-free position spacing
        var sourceCards = db.Cards
            .Where(c => c.LaneId == lane1 && (c.Id == card1Id || c.Id == card3Id))
            .OrderBy(c => c.Position)
            .ToList();

        sourceCards.Count.ShouldBe(2);
        sourceCards.Select(c => c.Id).ShouldContain(card1Id);
        sourceCards.Select(c => c.Id).ShouldContain(card3Id);
        // Positions should be contiguous multiples of 10 (gap-free after reorder)
        (sourceCards[1].Position - sourceCards[0].Position).ShouldBe(10);
    }

    // ── Label replace via update_card ───────────────────────────────────────

    [Fact]
    public async Task UpdateCard_WithLabelIds_AssignsLabels()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        // Create labels
        var label1 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"McpLabel1-{Guid.NewGuid()}", Color = "red" };
        var label2 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"McpLabel2-{Guid.NewGuid()}", Color = "blue" };
        db.Labels.AddRange(label1, label2);
        await db.SaveChangesAsync();

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, labelIds: $"{label1.Id},{label2.Id}");

        // Assert
        result.ShouldNotContain("Error");
        var cardLabels = db.CardLabels.Where(cl => cl.CardId == cardId).Select(cl => cl.LabelId).ToList();
        cardLabels.Count.ShouldBe(2);
        cardLabels.ShouldContain(label1.Id);
        cardLabels.ShouldContain(label2.Id);
    }

    [Fact]
    public async Task UpdateCard_WithLabelIds_ReplacesExistingLabels()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        var label1 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"Replace1-{Guid.NewGuid()}", Color = "red" };
        var label2 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"Replace2-{Guid.NewGuid()}", Color = "green" };
        var label3 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"Replace3-{Guid.NewGuid()}", Color = "blue" };
        db.Labels.AddRange(label1, label2, label3);
        await db.SaveChangesAsync();

        // First: assign label1 and label2
        await tools.UpdateCardAsync(authKey, cardId, labelIds: $"{label1.Id},{label2.Id}");

        // Act — replace with label2 and label3 (label1 removed, label3 added, label2 kept)
        var result = await tools.UpdateCardAsync(authKey, cardId, labelIds: $"{label2.Id},{label3.Id}");

        // Assert
        result.ShouldNotContain("Error");
        var cardLabels = db.CardLabels.Where(cl => cl.CardId == cardId).Select(cl => cl.LabelId).ToList();
        cardLabels.Count.ShouldBe(2);
        cardLabels.ShouldContain(label2.Id);
        cardLabels.ShouldContain(label3.Id);
        cardLabels.ShouldNotContain(label1.Id);
    }

    [Fact]
    public async Task UpdateCard_WithEmptyLabelIds_ClearsAllLabels()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        var label = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"ClearMe-{Guid.NewGuid()}", Color = "red" };
        db.Labels.Add(label);
        await db.SaveChangesAsync();

        await tools.UpdateCardAsync(authKey, cardId, labelIds: label.Id.ToString());

        // Act — clear all labels
        var result = await tools.UpdateCardAsync(authKey, cardId, labelIds: "");

        // Assert
        result.ShouldNotContain("Error");
        var cardLabels = db.CardLabels.Where(cl => cl.CardId == cardId).ToList();
        cardLabels.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateCard_WithInvalidLabelId_ReturnsError()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        var bogusLabelId = Guid.NewGuid();

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, labelIds: bogusLabelId.ToString());

        // Assert
        result.ShouldContain("Error: Labels not found or not on the same board");
    }

    [Fact]
    public async Task UpdateCard_WithMalformedLabelIds_ReturnsError()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var cardId = await CreateCardInLaneAsync(tools, authKey, lanes[0]);

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, labelIds: "not-a-guid");

        // Assert
        result.ShouldContain("Error: Invalid label ID");
    }

    // ── create_card labelIds ────────────────────────────────────────────────

    [Fact]
    public async Task CreateCard_WithLabelIds_AssignsLabels()
    {
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var label1 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"CL1-{Guid.NewGuid()}", Color = "red" };
        var label2 = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"CL2-{Guid.NewGuid()}", Color = "blue" };
        db.Labels.AddRange(label1, label2);
        await db.SaveChangesAsync();

        var result = await tools.CreateCardAsync(authKey, "Labeled Card", lanes[0], labelIds: $"{label1.Id},{label2.Id}");

        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        var cardId = json.RootElement.GetProperty("id").GetGuid();
        var cardLabels = db.CardLabels.Where(cl => cl.CardId == cardId).Select(cl => cl.LabelId).ToList();
        cardLabels.Count.ShouldBe(2);
        cardLabels.ShouldContain(label1.Id);
        cardLabels.ShouldContain(label2.Id);
    }

    [Fact]
    public async Task CreateCard_WithNoLabelIds_CreatesCardWithoutLabels()
    {
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();

        var result = await tools.CreateCardAsync(authKey, "No Labels", lanes[0]);

        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        var cardId = json.RootElement.GetProperty("id").GetGuid();
        db.CardLabels.Where(cl => cl.CardId == cardId).ToList().ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateCard_WithNonexistentLabel_ReturnsError()
    {
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();

        var result = await tools.CreateCardAsync(authKey, "Bad Label", lanes[0], labelIds: Guid.NewGuid().ToString());

        result.ShouldContain("Error");
    }

    [Fact]
    public async Task CreateCard_WithInvalidLabelIdFormat_ReturnsError()
    {
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();

        var result = await tools.CreateCardAsync(authKey, "Bad Format", lanes[0], labelIds: "not-a-guid");

        result.ShouldContain("Error");
    }

    [Fact]
    public async Task CreateCard_WithEmptyLabelIds_CreatesCardWithoutLabels()
    {
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();

        var result = await tools.CreateCardAsync(authKey, "Empty Labels", lanes[0], labelIds: "");

        result.ShouldNotContain("Error");
    }

    // ── Hardened reorder/move tests ─────────────────────────────────────

    [Fact]
    public async Task MoveCard_SameLaneReorder_PositionsAreContiguous()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, _) = await GetTwoLaneIdsAsync(db);

        var card1Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Reorder A");
        var card2Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Reorder B");
        var card3Id = await CreateCardInLaneAsync(tools, authKey, lane1, "Reorder C");

        // Act — move card3 to index 0 (front) within same lane
        await tools.MoveCardAsync(authKey, lane1, cardId: card3Id, index: 0);

        // Assert — all cards in lane should have contiguous positions: 0, 10, 20
        var cards = db.Cards.Where(c => c.LaneId == lane1 && (c.Id == card1Id || c.Id == card2Id || c.Id == card3Id))
            .OrderBy(c => c.Position).ToList();
        cards.Count.ShouldBe(3);
        cards[0].Position.ShouldBe(0);
        cards[1].Position.ShouldBe(10);
        cards[2].Position.ShouldBe(20);
        cards[0].Id.ShouldBe(card3Id);
    }

    [Fact]
    public async Task MoveCard_CrossLane_SourceAndTargetBothReordered()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        var s1 = await CreateCardInLaneAsync(tools, authKey, lane1, "Source 1");
        var s2 = await CreateCardInLaneAsync(tools, authKey, lane1, "Source 2");
        var s3 = await CreateCardInLaneAsync(tools, authKey, lane1, "Source 3");
        var t1 = await CreateCardInLaneAsync(tools, authKey, lane2, "Target 1");

        // Act — move s2 to lane2 at index 0
        await tools.MoveCardAsync(authKey, lane2, cardId: s2, index: 0);

        // Assert — source lane: s1, s3 with positions 0, 10
        var sourceCards = db.Cards.Where(c => c.LaneId == lane1 && (c.Id == s1 || c.Id == s3))
            .OrderBy(c => c.Position).ToList();
        sourceCards.Count.ShouldBe(2);
        sourceCards[0].Position.ShouldBe(0);
        sourceCards[1].Position.ShouldBe(10);

        // Assert — target lane: s2 at front, t1 after
        var targetCards = db.Cards.Where(c => c.LaneId == lane2 && (c.Id == s2 || c.Id == t1))
            .OrderBy(c => c.Position).ToList();
        targetCards.Count.ShouldBe(2);
        targetCards[0].Id.ShouldBe(s2);
        targetCards[0].Position.ShouldBe(0);
        targetCards[1].Position.ShouldBe(10);
    }

    [Fact]
    public async Task MoveCard_IndexBeyondEnd_ClampsToEnd()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        var existing = await CreateCardInLaneAsync(tools, authKey, lane2, "Existing");
        var card = await CreateCardInLaneAsync(tools, authKey, lane1, "Clamp Test");

        // Count cards already in lane2 (excluding the card being moved)
        var existingCount = db.Cards.Count(c => c.LaneId == lane2);

        // Act — move with index 999 (way beyond end)
        var result = await tools.MoveCardAsync(authKey, lane2, cardId: card, index: 999);

        // Assert — clamped to end (after all existing cards)
        result.ShouldContain($"moved to lane at index {existingCount}");
    }

    [Fact]
    public async Task MoveCard_NegativeIndex_ClampsToZero()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);

        var card = await CreateCardInLaneAsync(tools, authKey, lane1, "Negative Test");

        // Act
        var result = await tools.MoveCardAsync(authKey, lane2, cardId: card, index: -5);

        // Assert
        result.ShouldContain("moved to lane at index 0");
    }

    [Fact]
    public async Task UpdateCard_MoveWithinSameLane_PositionsContiguous()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var lanes = db.Lanes.Where(l => l.BoardId == _factory.DefaultBoardId).Select(l => l.Id).ToList();
        var lane = lanes[0];

        var c1 = await CreateCardInLaneAsync(tools, authKey, lane, "Same Lane A");
        var c2 = await CreateCardInLaneAsync(tools, authKey, lane, "Same Lane B");
        var c3 = await CreateCardInLaneAsync(tools, authKey, lane, "Same Lane C");

        // Act — move c1 to index 2 (end) via update_card
        await tools.UpdateCardAsync(authKey, c1, laneId: lane, index: 2);

        // Assert
        var cards = db.Cards.Where(c => c.LaneId == lane && (c.Id == c1 || c.Id == c2 || c.Id == c3))
            .OrderBy(c => c.Position).ToList();
        cards.Count.ShouldBe(3);
        cards[0].Position.ShouldBe(0);
        cards[1].Position.ShouldBe(10);
        cards[2].Position.ShouldBe(20);
        cards[2].Id.ShouldBe(c1);
    }

    [Fact]
    public async Task MoveCard_EmptyTargetLane_CardGetsPositionZero()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, _) = await GetTwoLaneIdsAsync(db);

        // Create a fresh empty lane to guarantee no pre-existing cards
        var emptyLane = new Lane { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = "Empty Lane", Position = 999 };
        db.Lanes.Add(emptyLane);
        await db.SaveChangesAsync();

        var card = await CreateCardInLaneAsync(tools, authKey, lane1, "Empty Target");

        // Act
        await tools.MoveCardAsync(authKey, emptyLane.Id, cardId: card);

        // Assert
        var movedCard = await db.Cards.FindAsync(card);
        movedCard!.LaneId.ShouldBe(emptyLane.Id);
        movedCard.Position.ShouldBe(0);
    }

    // ── Combined operations ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCard_MoveAndRenameAndLabel_AllApplied()
    {
        // Arrange
        var (db, tools, authKey) = CreateTools();
        var (lane1, lane2) = await GetTwoLaneIdsAsync(db);
        var cardId = await CreateCardInLaneAsync(tools, authKey, lane1, "Original");

        var label = new Label { Id = Guid.NewGuid(), BoardId = _factory.DefaultBoardId, Name = $"Combo-{Guid.NewGuid()}", Color = "green" };
        db.Labels.Add(label);
        await db.SaveChangesAsync();

        // Act
        var result = await tools.UpdateCardAsync(authKey, cardId, name: "Renamed", laneId: lane2, labelIds: label.Id.ToString());

        // Assert
        result.ShouldNotContain("Error");
        var json = System.Text.Json.JsonDocument.Parse(result);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Renamed");
        json.RootElement.GetProperty("laneId").GetGuid().ShouldBe(lane2);

        var cardLabels = db.CardLabels.Where(cl => cl.CardId == cardId).Select(cl => cl.LabelId).ToList();
        cardLabels.ShouldContain(label.Id);
    }
}
