using Collaboard.Api;
using Collaboard.Api.Auth;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors();
builder.Services.AddDbContext<BoardDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Board") ?? "Data Source=collaboard.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        db.Users.Add(new BoardUser
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            AuthKey = Ulid.NewUlid().ToString(),
            Role = UserRole.Administrator,
        });

        db.Lanes.AddRange(
            new Lane { Id = Guid.NewGuid(), Name = "Backlog", Position = 0 },
            new Lane { Id = Guid.NewGuid(), Name = "In Progress", Position = 1 },
            new Lane { Id = Guid.NewGuid(), Name = "Done", Position = 2 }
        );
        await db.SaveChangesAsync();
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

api.MapGet("/board", async (BoardDbContext db, HttpContext http) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var lanes = await db.Lanes.OrderBy(x => x.Position).ToListAsync();
    var cards = await db.Cards.OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();
    return Results.Ok(new { lanes, cards });
});

api.MapPost("/users", async (BoardDbContext db, HttpContext http, BoardUser request) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
    if (forbidden is not null) return forbidden;

    var user = new BoardUser
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Role = request.Role,
        AuthKey = Ulid.NewUlid().ToString(),
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/users/{user.Id}", user);
});

api.MapGet("/users", async (BoardDbContext db, HttpContext http) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
    if (forbidden is not null) return forbidden;
    return Results.Ok(await db.Users.OrderBy(x => x.Name).ToListAsync());
});

api.MapPost("/lanes", async (BoardDbContext db, HttpContext http, Lane request) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
    if (forbidden is not null) return forbidden;

    var lane = new Lane { Id = Guid.NewGuid(), Name = request.Name, Position = request.Position };
    db.Lanes.Add(lane);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/lanes/{lane.Id}", lane);
});

api.MapDelete("/lanes/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
    if (forbidden is not null) return forbidden;

    var lane = await db.Lanes.FindAsync(id);
    if (lane is null) return Results.NotFound();
    if (await db.Cards.AnyAsync(x => x.LaneId == id)) return Results.Conflict("Lane must be empty.");
    db.Lanes.Remove(lane);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

api.MapPost("/cards", async (BoardDbContext db, HttpContext http, CardItem request) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var now = DateTimeOffset.UtcNow;
    var currentUser = http.CurrentUser();
    var nextNumber = (await db.Cards.MaxAsync(x => (long?)x.Number) ?? 0) + 1;
    var card = new CardItem
    {
        Id = Guid.NewGuid(),
        Number = nextNumber,
        Name = request.Name,
        DescriptionMarkdown = request.DescriptionMarkdown,
        Status = request.Status,
        Size = request.Size,
        LaneId = request.LaneId,
        Position = request.Position,
        CreatedAtUtc = now,
        LastUpdatedAtUtc = now,
        CreatedByUserId = currentUser.Id,
        LastUpdatedByUserId = currentUser.Id,
    };
    db.Cards.Add(card);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/cards/{card.Id}", card);
});

api.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, CardItem patch) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var card = await db.Cards.FindAsync(id);
    if (card is null) return Results.NotFound();

    if (!string.IsNullOrEmpty(patch.Name)) card.Name = patch.Name;
    if (!string.IsNullOrEmpty(patch.DescriptionMarkdown)) card.DescriptionMarkdown = patch.DescriptionMarkdown;
    if (patch.Status is not null) card.Status = patch.Status;
    if (!string.IsNullOrEmpty(patch.Size)) card.Size = patch.Size;
    if (patch.LaneId != Guid.Empty) card.LaneId = patch.LaneId;
    if (patch.Position != 0) card.Position = patch.Position;
    card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
    card.LastUpdatedByUserId = http.CurrentUser().Id;
    await db.SaveChangesAsync();
    return Results.Ok(card);
});

api.MapDelete("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var card = await db.Cards.FindAsync(id);
    if (card is null) return Results.NotFound();
    db.Cards.Remove(card);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

api.MapPost("/cards/{id:guid}/comments", async (BoardDbContext db, HttpContext http, Guid id, CardComment request) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    if (!await db.Cards.AnyAsync(x => x.Id == id)) return Results.NotFound();
    var comment = new CardComment
    {
        Id = Guid.NewGuid(),
        CardId = id,
        UserId = http.CurrentUser().Id,
        ContentMarkdown = request.ContentMarkdown,
        LastUpdatedAtUtc = DateTimeOffset.UtcNow,
    };
    db.Comments.Add(comment);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/cards/{id}/comments/{comment.Id}", comment);
});

api.MapDelete("/comments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var comment = await db.Comments.FindAsync(id);
    if (comment is null) return Results.NotFound();
    var user = http.CurrentUser();
    if (comment.UserId != user.Id && user.Role != UserRole.Administrator) return Results.Forbid();
    db.Comments.Remove(comment);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

api.MapPost("/cards/{id:guid}/attachments", async (BoardDbContext db, HttpContext http, Guid id, IFormFile file) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    if (!await db.Cards.AnyAsync(x => x.Id == id)) return Results.NotFound();

    await using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var attachment = new CardAttachment
    {
        Id = Guid.NewGuid(),
        CardId = id,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Payload = ms.ToArray(),
        AddedByUserId = http.CurrentUser().Id,
        AddedAtUtc = DateTimeOffset.UtcNow,
    };
    db.Attachments.Add(attachment);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/cards/{id}/attachments/{attachment.Id}", new { attachment.Id, attachment.FileName });
});

api.MapGet("/attachments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;

    var attachment = await db.Attachments.FindAsync(id);
    return attachment is null ? Results.NotFound() : Results.File(attachment.Payload, attachment.ContentType, attachment.FileName);
});

api.MapDelete("/attachments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
{
    var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.HumanUser);
    if (forbidden is not null) return forbidden;
    var attachment = await db.Attachments.FindAsync(id);
    if (attachment is null) return Results.NotFound();
    db.Attachments.Remove(attachment);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapAgentManifest();

app.Run();
