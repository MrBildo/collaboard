using System.Globalization;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    // #234: SQLite's default DateTimeOffset mapping cannot be translated when
    // the comparison appears in a nested query position (correlated sub-query,
    // set operation), which broke the get_cards `since` activity filter.
    // Storing DateTimeOffset as a normalized-UTC round-trippable ISO-8601
    // string keeps the column TEXT (no column-type migration) while making
    // `>=` a plain string comparison SQLite translates in any position.
    // "O" on a UTC DateTimeOffset is fixed-width and lexicographically
    // ordered, so string ordering matches chronological ordering.
    private static readonly ValueConverter<DateTimeOffset, string> _sortableUtcConverter = new
    (
        v => v.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    );

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Board>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<BoardUser>().HasIndex(x => x.AuthKey).IsUnique();
        builder.Entity<Lane>().HasIndex(x => new { x.BoardId, x.Position }).IsUnique();
        builder.Entity<CardSize>().HasIndex(x => new { x.BoardId, x.Ordinal }).IsUnique();
        builder.Entity<CardSize>().HasIndex(x => new { x.BoardId, x.Name }).IsUnique();
        builder.Entity<CardItem>().HasIndex(x => new { x.BoardId, x.Number }).IsUnique().HasFilter("\"Number\" > 0");
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

        builder.Entity<CardItem>()
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.LastUpdatedByUserId)
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

        // #234: sortable-UTC string conversion for every DateTimeOffset column.
        // Applied model-wide (not just the columns the `since` filter touches)
        // so the storage format is uniform and any future nested date predicate
        // translates too. Column stays TEXT, so this is a format change, not a
        // column-type change.
        builder.Entity<Board>().Property(x => x.CreatedAtUtc).HasConversion(_sortableUtcConverter);
        builder.Entity<CardItem>().Property(x => x.CreatedAtUtc).HasConversion(_sortableUtcConverter);
        builder.Entity<CardItem>().Property(x => x.LastUpdatedAtUtc).HasConversion(_sortableUtcConverter);
        builder.Entity<CardComment>().Property(x => x.LastUpdatedAtUtc).HasConversion(_sortableUtcConverter);
        builder.Entity<CardAttachment>().Property(x => x.AddedAtUtc).HasConversion(_sortableUtcConverter);
    }
}
