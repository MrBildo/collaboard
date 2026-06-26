using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collaboard.Api.Persistence;

// Per-entity EF model configuration for the card aggregate and its children (comments, attachments,
// label join). Applied by BoardDbContext via ApplyConfigurationsFromAssembly. Each type is a sealed
// leaf IEntityTypeConfiguration — no subtype hierarchy is intended.

internal sealed class CardItemConfiguration : IEntityTypeConfiguration<CardItem>
{
    public void Configure(EntityTypeBuilder<CardItem> builder)
    {
        builder.HasIndex(x => new { x.BoardId, x.Number }).IsUnique().HasFilter("\"Number\" > 0");
        builder.HasIndex(x => new { x.LaneId, x.Position });
        builder.HasIndex(x => x.CreatedByUserId);
        builder.HasIndex(x => x.LastUpdatedByUserId);

        builder.Property(x => x.Name).HasMaxLength(120);

        builder.Property(x => x.CreatedAtUtc).HasConversion(ValueConverters.SortableUtc);
        builder.Property(x => x.LastUpdatedAtUtc).HasConversion(ValueConverters.SortableUtc);

        builder
            .HasOne<Lane>().WithMany()
            .HasForeignKey(x => x.LaneId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<CardSize>().WithMany()
            .HasForeignKey(x => x.SizeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.LastUpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CardCommentConfiguration : IEntityTypeConfiguration<CardComment>
{
    public void Configure(EntityTypeBuilder<CardComment> builder)
    {
        builder.HasIndex(x => new { x.CardId, x.LastUpdatedAtUtc });
        builder.HasIndex(x => x.UserId);

        builder.Property(x => x.LastUpdatedAtUtc).HasConversion(ValueConverters.SortableUtc);

        builder
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CardAttachmentConfiguration : IEntityTypeConfiguration<CardAttachment>
{
    public void Configure(EntityTypeBuilder<CardAttachment> builder)
    {
        builder.HasIndex(x => x.CardId);
        builder.HasIndex(x => x.AddedByUserId);

        builder.Property(x => x.FileName).HasMaxLength(240);
        builder.Property(x => x.ContentType).HasMaxLength(100);

        builder.Property(x => x.AddedAtUtc).HasConversion(ValueConverters.SortableUtc);

        builder
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<BoardUser>().WithMany()
            .HasForeignKey(x => x.AddedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CardLabelConfiguration : IEntityTypeConfiguration<CardLabel>
{
    public void Configure(EntityTypeBuilder<CardLabel> builder)
    {
        builder.HasKey(x => new { x.CardId, x.LabelId });

        builder
            .HasOne<CardItem>().WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<Label>().WithMany()
            .HasForeignKey(x => x.LabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
