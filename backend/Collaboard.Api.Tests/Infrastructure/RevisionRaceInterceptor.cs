using Collaboard.Api.Endpoints;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Collaboard.Api.Tests.Infrastructure;

// Forces a description-revision collision from inside a real request.
//
// The collision lives in the gap between a write path reading the trail's head and the save that
// inserts the row it allocated. Two genuinely parallel requests cannot open that gap on demand, and
// the harness's single shared connection makes racing them fail on connection contention instead.
// This opens it deterministically: when an armed card's save is about to run, a rival edit commits
// first on its own scope, so the request that follows finds its revision taken.
//
// What that buys is coverage of the wiring rather than the mechanism. A test that calls the history
// helper directly proves the retry works; it says nothing about whether the endpoints still reach
// it, so the retry could be deleted from an entry point with every test still green. Reaching the
// collision through the entry point is what makes that deletion visible.
public class RevisionRaceInterceptor(IServiceScopeFactory scopeFactory) : SaveChangesInterceptor
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory
        ?? throw new ArgumentNullException(nameof(scopeFactory));

    private Guid _armedCardId;
    private Guid _rivalUserId;
    private string _rivalValue = string.Empty;
    private int _fired;

    public bool HasFired => Volatile.Read(ref _fired) > 0;

    // Armed per test rather than per factory: the fixture is shared across the class, and a latch
    // left set would let the second test pass without ever meeting a collision.
    public void Arm(Guid cardId, Guid rivalUserId, string rivalValue)
    {
        _armedCardId = cardId;
        _rivalUserId = rivalUserId;
        _rivalValue = rivalValue;
        Volatile.Write(ref _fired, 0);
    }

    public void Disarm()
    {
        _armedCardId = Guid.Empty;
        Volatile.Write(ref _fired, 0);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync
    (
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (ShouldFire(eventData.Context))
        {
            await CommitRivalEditAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private bool ShouldFire(DbContext? context)
    {
        if (context is null || _armedCardId == Guid.Empty)
        {
            return false;
        }

        var stagesARevision = context.ChangeTracker
            .Entries<CardFieldHistory>()
            .Any(e => e.State == EntityState.Added && e.Entity.CardId == _armedCardId);

        // Claiming the latch here is also what stops the rival's own save from re-entering: it
        // stages a revision on the same card and would otherwise arm a race against itself.
        return stagesARevision && Interlocked.Exchange(ref _fired, 1) == 0;
    }

    private async Task CommitRivalEditAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();

        // Read the committed description, not the one the intercepted request is holding: the rival
        // is a separate editor who never saw it.
        var card = await db.Cards.FirstAsync(c => c.Id == _armedCardId, ct);
        var oldValue = card.DescriptionMarkdown;

        card.DescriptionMarkdown = _rivalValue;
        card.LastUpdatedByUserId = _rivalUserId;
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;

        var change = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            db,
            _armedCardId,
            oldValue,
            _rivalValue,
            _rivalUserId,
            ct
        );

        await CardHistoryHelper.SaveWithRevisionRetryAsync(db, change, ct);
    }
}
