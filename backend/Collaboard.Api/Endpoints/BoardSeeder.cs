using Collaboard.Api.Models;

namespace Collaboard.Api.Endpoints;

// Shared board-seed logic for the REST BoardEndpoints (POST /boards) and the MCP
// BoardTools (create_board). A new board is seeded with one hidden archive lane
// (Position = int.MaxValue, IsArchiveLane = true) and the four default card sizes
// (S/M/L/XL, ordinals 0-3). The seed was byte-identical in both front doors; one
// domain fact belongs in one place. REST/MCP drift is the top bug class on this
// codebase (see PruneFilter), so the create-board seed is shared on the same
// precedent.
//
// Seed does NOT call SaveChanges — the caller owns the transaction (it also adds
// the Board itself and persists everything in one save).
internal static class BoardSeeder
{
    public static void Seed(BoardDbContext db, Board board)
    {
        db.Lanes.Add(new Lane
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Archive",
            Position = int.MaxValue,
            IsArchiveLane = true,
        });

        db.CardSizes.AddRange
        (
            new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "S", Ordinal = 0 },
            new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "M", Ordinal = 1 },
            new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "L", Ordinal = 2 },
            new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "XL", Ordinal = 3 }
        );
    }
}
