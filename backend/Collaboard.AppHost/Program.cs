var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Collaboard_Api>("api")
    .WithHttpHealthCheck("/health");

builder.AddViteApp("frontend", "../../frontend")
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
