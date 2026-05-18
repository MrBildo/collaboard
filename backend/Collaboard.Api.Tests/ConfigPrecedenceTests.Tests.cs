using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Collaboard.Api.Tests;

// Proves the configuration-provider precedence Program.cs establishes:
//
//   env (Section__Key) > appsettings.Local.json > appsettings.json > hardcoded default
//
// The fix (#225) re-adds AddEnvironmentVariables() AFTER the appsettings.Local.json
// load so an operator overlay no longer shadows a Collabhost-injected env var.
//
// This is tested by replicating Program.cs's exact provider sequence against a real
// ConfigurationBuilder (the HostingBindResolverTests pattern) rather than the WAF:
// WebApplicationFactory injects in-memory providers via UseSetting /
// ConfigureAppConfiguration, which structurally cannot exercise the AddJsonFile ->
// AddEnvironmentVariables ordering this fix changes. Temp files + a scoped real env
// var are the only seam that proves file-vs-env ordering.
//
// ProgramConfigChain mirrors Program.cs:23-32. The source-regression guard at the
// bottom asserts the env re-add still follows the .Local.json load, so a future
// Program.cs refactor that reorders or drops the line fails loudly here.
public sealed class ConfigPrecedenceTests : IDisposable
{
    private const string Key = "ConfigPrecedence:Probe";
    private const string EnvKey = "ConfigPrecedence__Probe";

    private readonly string _dir;
    private readonly string _appsettingsPath;
    private readonly string _localPath;

    public ConfigPrecedenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"cfgprec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _appsettingsPath = Path.Combine(_dir, "appsettings.json");
        _localPath = Path.Combine(_dir, "appsettings.Local.json");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvKey, null);
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    // Mirrors Program.cs:23-32 — the framework default chain (incl. an env-var
    // provider added at builder-construction time), then appsettings.Local.json,
    // then the #225 env-var re-add that pushes env back to the top.
    private IConfiguration BuildProgramConfigChain()
    {
        var builder = new ConfigurationBuilder();

        // appsettings.json — the shipped default tier.
        builder.AddJsonFile(_appsettingsPath, optional: true, reloadOnChange: false);

        // The env-var provider WebApplication.CreateBuilder adds at construction time.
        // Present here so the test proves the #225 re-add is what fixes ordering, not
        // merely that an env provider exists at all.
        builder.AddEnvironmentVariables();

        // Program.cs:25 — the operator overlay, highest until the re-add below.
        builder.AddJsonFile(_localPath, optional: true, reloadOnChange: false);

        // Program.cs — the #225 fix: re-add env vars AFTER .Local.json.
        builder.AddEnvironmentVariables();

        return builder.Build();
    }

    // Program.cs's "hardcoded default" tier is a settings-POCO initializer or a
    // GetValue(..., literal) fallback, not a configuration provider — i.e. a
    // null-coalesce on the resolved config value. Model it exactly that way.
    private string Resolve(IConfiguration config, string hardcodedDefault) =>
        config[Key] ?? hardcodedDefault;

    private void WriteAppsettings(string value) =>
        File.WriteAllText(_appsettingsPath, $$"""
            { "ConfigPrecedence": { "Probe": "{{value}}" } }
            """);

    private void WriteLocal(string value) =>
        File.WriteAllText(_localPath, $$"""
            { "ConfigPrecedence": { "Probe": "{{value}}" } }
            """);

    [Fact]
    public void EnvVar_WinsOver_AppsettingsLocalJson()
    {
        WriteAppsettings("from-appsettings");
        WriteLocal("from-local");
        Environment.SetEnvironmentVariable(EnvKey, "from-env");

        var resolved = Resolve(BuildProgramConfigChain(), "hardcoded-default");

        resolved.ShouldBe("from-env");
    }

    [Fact]
    public void AppsettingsLocalJson_WinsOver_AppsettingsJson()
    {
        WriteAppsettings("from-appsettings");
        WriteLocal("from-local");

        var resolved = Resolve(BuildProgramConfigChain(), "hardcoded-default");

        resolved.ShouldBe("from-local");
    }

    [Fact]
    public void AppsettingsJson_WinsOver_HardcodedDefault()
    {
        WriteAppsettings("from-appsettings");

        var resolved = Resolve(BuildProgramConfigChain(), "hardcoded-default");

        resolved.ShouldBe("from-appsettings");
    }

    [Fact]
    public void HardcodedDefault_WinsWhenNoProviderSuppliesTheKey()
    {
        var resolved = Resolve(BuildProgramConfigChain(), "hardcoded-default");

        resolved.ShouldBe("hardcoded-default");
    }

    [Fact]
    public void EnvVar_WinsOver_AppsettingsJson_WhenNoLocalOverlay()
    {
        WriteAppsettings("from-appsettings");
        Environment.SetEnvironmentVariable(EnvKey, "from-env");

        var resolved = Resolve(BuildProgramConfigChain(), "hardcoded-default");

        resolved.ShouldBe("from-env");
    }

    // Source-level regression lock: the entire #225 fix is the ordering of two
    // Program.cs lines. A refactor that reorders them, or drops the re-add,
    // silently reintroduces the .Local.json-shadows-env inversion with zero
    // behavioral test able to catch it (the WAF cannot exercise file-vs-env
    // ordering). Pin the ordering against the source itself.
    [Fact]
    public void ProgramCs_ReaddsEnvVarsAfterLocalJsonLoad()
    {
        var programPath = Path.Combine
        (
            FindRepoRoot(),
            "backend",
            "Collaboard.Api",
            "Program.cs"
        );

        File.Exists(programPath)
            .ShouldBeTrue($"Program.cs not found at {programPath}");

        var source = File.ReadAllText(programPath);

        var localLoadIndex = source.IndexOf
        (
            "AddJsonFile(\"appsettings.Local.json\"",
            StringComparison.Ordinal
        );
        var envReaddIndex = source.IndexOf
        (
            "AddEnvironmentVariables()",
            StringComparison.Ordinal
        );

        localLoadIndex.ShouldBeGreaterThan
        (
            -1,
            "appsettings.Local.json load missing from Program.cs"
        );
        envReaddIndex.ShouldBeGreaterThan
        (
            -1,
            "AddEnvironmentVariables() re-add missing from Program.cs — #225 "
            + "inversion reintroduced: .Local.json would shadow env vars"
        );
        envReaddIndex.ShouldBeGreaterThan
        (
            localLoadIndex,
            "AddEnvironmentVariables() must appear AFTER the appsettings.Local.json "
            + "load (#225) — otherwise the operator overlay shadows env vars"
        );
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("Could not locate repo root (.git) from test base dir");
        return dir.FullName;
    }
}
