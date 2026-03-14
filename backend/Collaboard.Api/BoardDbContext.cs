using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api;

public class BoardDbContext(DbContextOptions<BoardDbContext> options) : DbContext(options)
{
    public DbSet<BoardUser> Users => Set<BoardUser>();
    public DbSet<Lane> Lanes => Set<Lane>();
    public DbSet<CardItem> Cards => Set<CardItem>();
    public DbSet<CardComment> Comments => Set<CardComment>();
    public DbSet<CardAttachment> Attachments => Set<CardAttachment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<BoardUser>().HasIndex(x => x.AuthKey).IsUnique();
        builder.Entity<Lane>().HasIndex(x => x.Position).IsUnique();
        builder.Entity<CardItem>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<CardItem>().HasIndex(x => new { x.LaneId, x.Position }).IsUnique();
        builder.Entity<CardComment>().HasIndex(x => new { x.CardId, x.LastUpdatedAtUtc });
        builder.Entity<CardAttachment>().HasIndex(x => x.CardId);
        builder.Entity<Label>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<CardLabel>().HasKey(x => new { x.CardId, x.LabelId });
    }
}
