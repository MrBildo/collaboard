namespace Collabot.Collattice.Api.Configuration;

public class CorsSettings
{
    public const string SectionName = "Cors";

    // Empty default = no cross-origin requests allowed. Same-origin LAN release still
    // works because the browser doesn't issue a preflight for same-origin requests.
    //
    // Env-var override syntax under hosted-separately deployment:
    //   Cors__AllowedOrigins__0=https://collattice.example.com
    //   Cors__AllowedOrigins__1=https://collattice-staging.example.com
    // .NET's configuration binder reads __N__ indexed env vars as array elements.
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}
