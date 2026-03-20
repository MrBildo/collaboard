using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Collaboard.Api.Models;

public enum UserRole
{
    Administrator,
    HumanUser,
    AgentUser,
}

public class Board
{
    public Guid Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(80)] public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public static string GenerateSlug(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
}

public class BoardUser
{
    public Guid Id { get; set; }
    [MaxLength(26)] public string AuthKey { get; set; } = string.Empty;
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Lane
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    [MaxLength(40)] public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
}

public class CardSize
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    [MaxLength(20)] public string Name { get; set; } = string.Empty;
    public int Ordinal { get; set; }
}

public class CardItem
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public Guid BoardId { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public Guid SizeId { get; set; }
    public Guid LaneId { get; set; }
    public int Position { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid LastUpdatedByUserId { get; set; }
    public DateTimeOffset LastUpdatedAtUtc { get; set; }
}

public class CardComment
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid UserId { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public DateTimeOffset LastUpdatedAtUtc { get; set; }
}

public class Label
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    [MaxLength(30)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string? Color { get; set; }
}

public class CardLabel
{
    public Guid CardId { get; set; }
    public Guid LabelId { get; set; }
}

public class CardAttachment
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    [MaxLength(240)] public string FileName { get; set; } = string.Empty;
    [MaxLength(100)] public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Payload { get; set; } = [];
    public Guid AddedByUserId { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
}

public record CardLabelSummary(Guid Id, string Name, string? Color);

public record CardSummary(
    Guid Id,
    long Number,
    string Name,
    string DescriptionMarkdown,
    Guid SizeId,
    string SizeName,
    Guid LaneId,
    int Position,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    Guid LastUpdatedByUserId,
    DateTimeOffset LastUpdatedAtUtc,
    List<CardLabelSummary> Labels,
    int CommentCount,
    int AttachmentCount);

public record SearchResult(Guid BoardId, string BoardName, string BoardSlug, List<CardSummary> Cards);
