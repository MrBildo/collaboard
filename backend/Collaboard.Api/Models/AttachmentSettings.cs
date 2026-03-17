namespace Collaboard.Api.Models;

public record AttachmentSettings
{
    public int MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;
}
