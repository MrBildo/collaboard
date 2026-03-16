using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api;

public class BoardDbContext(DbContextOptions<BoardDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardUser> Users => Set<BoardUser>();
    public DbSet<Lane> Lanes => Set<Lane>();
    public DbSet<CardSize> CardSizes => Set<CardSize>();
    public DbSet<CardItem> Cards => Set<CardItem>();
    public DbSet<CardComment> Comments => Set<CardComment>();
    public DbSet<CardAttachment> Attachments => Set<CardAttachment>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<CardLabel> CardLabels => Set<CardLabel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Board>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<BoardUser>().HasIndex(x => x.AuthKey).IsUnique();
        builder.Entity<Lane>().HasIndex(x => new { x.BoardId, x.Position }).IsUnique();
        builder.Entity<CardSize>().HasIndex(x => new { x.BoardId, x.Ordinal }).IsUnique();
        builder.Entity<CardSize>().HasIndex(x => new { x.BoardId, x.Name }).IsUnique();
        builder.Entity<CardItem>().HasIndex(x => new { x.BoardId, x.Number }).IsUnique();
        builder.Entity<CardItem>().HasIndex(x => new { x.LaneId, x.Position });
        builder.Entity<CardComment>().HasIndex(x => new { x.CardId, x.LastUpdatedAtUtc });
        builder.Entity<CardAttachment>().HasIndex(x => x.CardId);
        builder.Entity<Label>().HasIndex(x => new { x.BoardId, x.Name }).IsUnique();
        builder.Entity<CardLabel>().HasKey(x => new { x.CardId, x.LabelId });

        // FK relationships
        builder.Entity<Lane>()
            .HasOne<Board>().WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardSize>()
            .HasOne<Board>().WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Label>()
            .HasOne<Board>().WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardItem>()
            .HasOne<Lane>().WithMany()
            .HasForeignKey(x => x.LaneId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardItem>()
            .HasOne<CardSize>().WithMany()
            .HasForeignKey(x => x.SizeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CardItem>()
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CardComment>()
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardComment>()
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CardLabel>()
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardLabel>()
            .HasOne<Label>().WithMany()
            .HasForeignKey(x => x.LabelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardAttachment>()
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CardAttachment>()
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.AddedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Additional indexes for FK columns
        builder.Entity<CardItem>().HasIndex(x => x.CreatedByUserId);
        builder.Entity<CardItem>().HasIndex(x => x.LastUpdatedByUserId);
        builder.Entity<CardComment>().HasIndex(x => x.UserId);
        builder.Entity<CardAttachment>().HasIndex(x => x.AddedByUserId);
    }
}
