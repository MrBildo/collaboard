using System.ComponentModel.DataAnnotations;

namespace Collaboard.Api.Models;

public enum UserRole
{
    Administrator,
    HumanUser,
    AgentUser,
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
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
}

public class CardItem
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    [MaxLength(20)] public string Size { get; set; } = "M";
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
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
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
