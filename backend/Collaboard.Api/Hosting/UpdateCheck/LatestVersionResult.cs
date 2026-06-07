namespace Collaboard.Api.Hosting.UpdateCheck;

// The fact a version source reports: the latest stable release tag and where an operator
// goes to get it. Deliberately small (#303 §2A) — a version string and a URL is the whole
// contract today. When a second thing is worth broadcasting (min-supported, a security flag)
// the source seam (ILatestVersionSource) lets a richer source replace GitHub without the
// endpoint or frontend changing.
public sealed record LatestVersionResult(string Version, string? ReleaseUrl);
