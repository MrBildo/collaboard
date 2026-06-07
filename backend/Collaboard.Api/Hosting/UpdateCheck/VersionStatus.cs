namespace Collaboard.Api.Hosting.UpdateCheck;

// The shape the /version/status endpoint serves (#303 §5). current is always known (the
// running assembly's stamped version); latest/releaseUrl/updateAvailable are populated once a
// poll has succeeded; lastChecked carries the timestamp of the last successful poll so the
// absence of a recent check is expressible. A poll that never succeeded (offline, disabled,
// pre-first-tick) leaves latest null and updateAvailable false — the honest degraded state.
public sealed record VersionStatus
(
    string Current,
    string? Latest,
    bool UpdateAvailable,
    string? ReleaseUrl,
    DateTimeOffset? LastChecked
);
