namespace Collaboard.Api.Configuration;

public class TempCardSweepSettings
{
    public const string SectionName = "TempCardSweep";

    // Temp cards older than this are orphans (the create-temp → finalize/cancel flow
    // is interactive and short-lived; a card sitting in temp state past this window
    // is one whose browser closed mid-flow). Overridable via TempCardSweep__Ttl.
    public TimeSpan Ttl { get; init; } = TimeSpan.FromHours(1);

    // How often the sweep runs. Overridable via TempCardSweep__SweepInterval.
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromMinutes(15);

    // Master switch — set false to disable the sweep entirely (e.g. a deployment
    // that wants to retain temp cards for diagnostics). Overridable via
    // TempCardSweep__Enabled.
    public bool Enabled { get; init; } = true;
}
