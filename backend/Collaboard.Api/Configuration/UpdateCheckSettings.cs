namespace Collaboard.Api.Configuration;

public class UpdateCheckSettings
{
    public const string SectionName = "UpdateCheck";

    // The kill switch for outbound update checks. A self-hosted product making silent
    // outbound calls is a trust
    // liability unless the operator can turn it off — set UpdateCheck__Enabled=false and the
    // hosted service never starts, no GitHub egress ever happens, and the status endpoint
    // reports updateAvailable=false. Default-on: the feature is the point, and the only thing
    // consumed is a public version string for our own public repo over HTTPS (no auto-update).
    public bool Enabled { get; init; } = true;

    // Poll cadence. Update availability is not time-critical (a new release is interesting
    // within a day, not within a minute), so a long cadence keeps egress negligible and the
    // unauthenticated GitHub rate-limit headroom enormous (8h => 3 req/day, vs the 60/hr
    // per-IP ceiling). Overridable via UpdateCheck__IntervalHours.
    public int IntervalHours { get; init; } = 8;

    // The GitHub repository whose /releases/latest is the source of truth for "latest"
    // This is the same repo publish.yml cuts releases against, so there is
    // no new artifact to maintain and no new drift class. "owner/name" form. Overridable via
    // UpdateCheck__Repository. Note: this does NOT enable mirror routing — api.github.com is
    // fixed (the mirror/Source seam is deferred). The real air-gap kill switch
    // is UpdateCheck__Enabled=false, which prevents all outbound egress.
    public string Repository { get; init; } = "MrBildo/collaboard";
}
