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

// Card #263 added contentMarkdown as the canonical body param for add_comment.
// Card #272 removed the deprecated `content` alias — contentMarkdown is now the sole
// required param. These tests pin the canonical path and the empty-input guard.
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
    public async Task AddComment_WithContentMarkdown_PersistsBody()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act
        var result = await commentTools.AddCommentAsync(author.AuthKey, "Body via contentMarkdown", cardId: cardId);

        // Assert
        var commentId = ParseCommentId(result);
        var stored = await db.Comments.FindAsync(commentId);
        stored.ShouldNotBeNull();
        stored.ContentMarkdown.ShouldBe("Body via contentMarkdown");
    }

    [Fact]
    public async Task AddComment_WithWhitespaceContentMarkdown_ReturnsError()
    {
        // Arrange
        var (db, commentTools) = CreateTools();
        var author = await CreateUserAsync();
        var cardId = await CreateCardAsync(db, author);

        // Act
        var result = await commentTools.AddCommentAsync(author.AuthKey, "   ", cardId: cardId);

        // Assert
        result.ShouldStartWith("Error");
        result.ShouldContain("contentMarkdown");
    }
}
