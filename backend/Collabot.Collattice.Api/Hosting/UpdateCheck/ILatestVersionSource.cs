namespace Collabot.Collattice.Api.Hosting.UpdateCheck;

// The isolation seam. The update checker depends on this, not on GitHub directly,
// so the GitHub Releases API can be swapped for a self-published manifest or an
// air-gap mirror later without touching the hosted service, the endpoint, or the frontend.
// Returns null on any failure (network, rate-limit, offline, malformed payload) — the
// fail-quiet contract: "I couldn't check" is not "you're out of date", so a failed fetch
// leaves the last good cached value in place rather than surfacing an error.
public interface ILatestVersionSource
{
    Task<LatestVersionResult?> GetLatestAsync(CancellationToken cancellationToken);
}
