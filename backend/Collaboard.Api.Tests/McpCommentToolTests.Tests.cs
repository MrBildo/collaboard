using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

// Card #263: the MCP add_comment input parameter was bare `content`, while the
// rest of the surface uses `<noun>Markdown` (descriptionMarkdown, contentMarkdown)
// and add_comment itself *returns* contentMarkdown for the same value. The fix is
// an additive, non-breaking alias: add_comment accepts both `content` and a new
// canonical `contentMarkdown`, preferring contentMarkdown when both are supplied,
// while `content` keeps working for existing callers. These tests pin all three
// resolution paths.
public class McpCommentToolTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>, IDisposable
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly List<IServiceScope> _scopes = [];
    private static int _nextCardNumber = 9000;
    private static int _nextCardPosition = 0;

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private (BoardDbContext Db, CommentTools CommentTools) CreateTools()
    {
        var scope = _factory.Services.CreateScope();
        _scopes.Add(scope);
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var auth = new McpAuthService(new UserResolver(db));
        var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
        return (db, new CommentTools(db, auth, broadcaster));
    }

    private async Task<BoardUser> CreateUserAsync()
    {
        using var setupClient = _factory.CreateClient();
        return await TestAuthHelper.CreateUserAsync
        (
            setupClient,
            _factory,
            $"commenter-{Guid.NewGuid():N}",
            UserRole.HumanUser
        );
    }

    private async Task<Guid> CreateCardAsync(BoardDbContext db, BoardUser createdBy)
    {
        var board = await db.Boards.FirstAsync();
        var lane = await db.Lanes.FirstAsync(l => l.BoardId == board.Id && !l.IsArchiveLane);
        var defaultSize = await db.CardSizes
            .Where(s => s.BoardId == board.Id)
            .OrderBy(s => s.Ordinal)
                .FirstAsync();

        var card = new CardItem
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            LaneId = lane.Id,
            SizeId = defaultSize.Id,
            Name = $"Comment Alias Test Card {Guid.NewGuid():N}",
            Number = Interlocked.Increment(ref _nextCardNumber),
            Position = Interlocked.Increment(ref _nextCardPosition),
            CreatedByUserId = createdBy.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastUpdatedByUserId = createdBy.Id,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    private static Guid ParseCommentId(string result)
    {
        result.ShouldNotStartWith("Error");
        using var doc = JsonDocument.Parse(result);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task AddComment_WithLegacyContentParam_PersistsBody()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act — the legacy `content` param keeps working for existing callers
        var result = await commentTools.AddCommentAsync(author.AuthKey, content: "Body via content", cardId: cardId);

        // Assert
        var commentId = ParseCommentId(result);
        var stored = await db.Comments.FindAsync(commentId);
        stored.ShouldNotBeNull();
        stored.ContentMarkdown.ShouldBe("Body via content");
    }

    [Fact]
    public async Task AddComment_WithCanonicalContentMarkdownParam_PersistsBody()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act — the new canonical `contentMarkdown` param resolves to the same body
        var result = await commentTools.AddCommentAsync(author.AuthKey, contentMarkdown: "Body via contentMarkdown", cardId: cardId);

        // Assert
        var commentId = ParseCommentId(result);
        var stored = await db.Comments.FindAsync(commentId);
        stored.ShouldNotBeNull();
        stored.ContentMarkdown.ShouldBe("Body via contentMarkdown");
    }

    [Fact]
    public async Task AddComment_BothParamsSupplied_PrefersContentMarkdown()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act — contentMarkdown wins when both are present
        var result = await commentTools.AddCommentAsync
        (
            author.AuthKey,
            content: "Loser via content",
            contentMarkdown: "Winner via contentMarkdown",
            cardId: cardId
        );

        // Assert
        var commentId = ParseCommentId(result);
        var stored = await db.Comments.FindAsync(commentId);
        stored.ShouldNotBeNull();
        stored.ContentMarkdown.ShouldBe("Winner via contentMarkdown");
    }

    [Fact]
    public async Task AddComment_NeitherParamSupplied_ReturnsError()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act
        var result = await commentTools.AddCommentAsync(author.AuthKey, cardId: cardId);

        // Assert
        result.ShouldStartWith("Error");
        result.ShouldContain("contentMarkdown");
    }
}
