using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Auth;

public class UserResolver(BoardDbContext db) : IUserResolver
{
    public Task<BoardUser?> ResolveAsync(string authKey, CancellationToken ct = default)
        => db.Users.SingleOrDefaultAsync(x => x.AuthKey == authKey && x.IsActive, ct);
}
