using Collaboard.Api;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddCors();
builder.Services.AddDbContext<BoardDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Board") ?? "Data Source=collaboard.db"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BoardEventBroadcaster>();
builder.Services.AddScoped<McpAuthService>();


builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'wal';");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");
    if (!await db.Users.AnyAsync())
    {
        var adminAuthKey = app.Configuration.GetValue<string>("Admin:AuthKey")
            ?? Ulid.NewUlid().ToString();

        db.Users.Add(new BoardUser
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            AuthKey = adminAuthKey,
            Role = UserRole.Administrator,
        });

        db.Lanes.AddRange(
            new Lane { Id = Guid.NewGuid(), Name = "Backlog", Position = 0 },
            new Lane { Id = Guid.NewGuid(), Name = "In Progress", Position = 1 },
            new Lane { Id = Guid.NewGuid(), Name = "Done", Position = 2 }
        );
        await db.SaveChangesAsync();

        app.Logger.LogInformation("Admin user seeded with auth key: {AuthKey}", adminAuthKey);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

var api = app.MapGroup("/api/v1");
api.MapBoardEndpoints();
api.MapUserEndpoints();
api.MapLaneEndpoints();
api.MapCardEndpoints();
api.MapLabelEndpoints();
api.MapCommentEndpoints();
api.MapAttachmentEndpoints();

app.MapEventEndpoints();

app.MapMcp();

app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
