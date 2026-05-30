var builder = DistributedApplication.CreateBuilder(args);

// ConnectionStrings:Board is required configuration with no fallback (#233 G-3b);
// the API hard-fails at startup if it is unset. The orchestrator is the "told
// input" for the dev run: an absolute path anchored on the AppHost's binary
// directory (stable per build, independent of the cwd `aspire start` is invoked
// from) so the dev database location does not depend on where the CLI was run.
var devDataPath = Path.Combine(AppContext.BaseDirectory, "data", "collaboard.db");

var api = builder.AddProject<Projects.Collaboard_Api>("api")
    .WithEnvironment("ConnectionStrings__Board", $"Data Source={devDataPath}")
    .WithHttpHealthCheck("/health");

builder.AddViteApp("frontend", "../../frontend")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

await builder.Build().RunAsync();
