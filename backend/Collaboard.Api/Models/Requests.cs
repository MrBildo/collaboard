namespace Collaboard.Api.Models;

public record CreateCommentRequest(string ContentMarkdown);

public record CreateUserRequest(string Name, UserRole Role);

public record CreateLabelRequest(string Name, string? Color);

public record CreateLaneRequest(string Name, int Position = 0);

public record CreateSizeRequest(string Name, int? Ordinal);
