using System.Globalization;
using System.Text.Json;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    public DbSet<WebhookDeliveryAttempt> WebhookDeliveryAttempts => Set<WebhookDeliveryAttempt>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

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

    // #326 — the webhook subscription event-selection is a small List<string> stored as a JSON TEXT
    // column (no child table; the set is tiny and read whole per drain). Replace-only at the store
    // (a fresh list is assigned on update, never mutated in place), so EF's reference-equality
    // change detection persists edits without a ValueComparer. NOTE: a value-converted column
    // defeats SQL translation of relational predicates — the dispatcher loads enabled rows and
    // matches the selection in CLR memory, never Where(s => s.EventTypes.Contains(...)).
    private static readonly ValueConverter<IList<string>, string> _eventTypesConverter = new
    (
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
    );

    // A value-converted mutable collection needs a comparer so EF compares by VALUE (not by
    // reference) for change detection — without it EF logs a model-validation warning and would
    // miss an in-place edit. The store treats the selection as replace-only anyway, but the comparer
    // makes the model correct regardless and silences the warning.
    private static readonly ValueComparer<IList<string>> _eventTypesComparer = new
    (
        (left, right) => (left == null && right == null)
            || (left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal)),
        value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(item))),
        value => value.ToList()
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

        // #320 — webhook delivery-attempt log indexes: the "deliveries for this board,
        // newest first" read, and "all attempts for one event".
        builder.Entity<WebhookDeliveryAttempt>().HasIndex(x => new { x.BoardId, x.AttemptedAtUtc });
        builder.Entity<WebhookDeliveryAttempt>().HasIndex(x => x.EventId);

        // #326 — serves the per-subscription "deliveries newest first" read and the on-read metrics
        // aggregation (success/failure counts + last-delivery per subscription).
        builder.Entity<WebhookDeliveryAttempt>().HasIndex(x => new { x.SubscriptionId, x.AttemptedAtUtc });

        // #326 — the subscription event-selection persists as a JSON TEXT column via the converter,
        // compared by value for change detection.
        builder.Entity<WebhookSubscription>()
            .Property(x => x.EventTypes)
            .HasConversion(_eventTypesConverter, _eventTypesComparer);

        // #326 — the delivery-attempt log's nullable SubscriptionId FK. SetNull (not Cascade):
        // deleting a subscription must NOT delete its delivery history — the audit log outlives the
        // subscription (an admin removing a flaky webhook still wants to see why it failed). A
        // deliberate divergence from the board-scoped Cascade relationships.
        builder.Entity<WebhookDeliveryAttempt>()
            .HasOne<WebhookSubscription>().WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.SetNull);

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
        builder.Entity<WebhookDeliveryAttempt>().Property(x => x.AttemptedAtUtc).HasConversion(_sortableUtcConverter);
    }
}
