namespace Collabot.Collattice.Api.Configuration;

public class HostingSettings
{
    public const string SectionName = "Hosting";

    // Default true preserves the single-process LAN release. Collabhost-hosted (and any
    // other split deployment) sets this false via env var: Hosting__ServeSpa=false.
    public bool ServeSpa { get; init; } = true;

    // LAN-first default: bind every interface so teammates on the same network can reach
    // the operator's machine. Collabhost-hosted deployments leave this alone — Collabhost
    // injects ASPNETCORE_URLS=http://localhost:{port}, which wins per the dual-pattern.
    public string ListenAddress { get; init; } = "0.0.0.0";

    public int ListenPort { get; init; } = 8080;
}
