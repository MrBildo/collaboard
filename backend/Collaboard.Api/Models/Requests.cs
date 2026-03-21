namespace Collaboard.Api.Models;

public record CreateCommentRequest(string ContentMarkdown);

public record CreateUserRequest(string Name, UserRole Role);

public record CreateLabelRequest(string Name, string? Color);

public record CreateLaneRequest(string Name, int Position = 0);

public record CreateSizeRequest(string Name, int? Ordinal);

// Board requests
public record CreateBoardRequest(string? Name);

public record UpdateBoardRequest(string? Name);

// Card requests
public record CreateCardRequest(Guid LaneId, string? Name, string? DescriptionMarkdown, int? Position, Guid? SizeId);

public record UpdateCardRequest(string? Name, string? DescriptionMarkdown, Guid? SizeId, Guid? LaneId, int? Position, Guid[]? LabelIds);

public record ReorderCardRequest(Guid? LaneId, int? Index);

// Comment requests
public record UpdateCommentRequest(string? ContentMarkdown);

// Label requests
public record UpdateLabelRequest(string? Name, string? Color);

public record AddCardLabelRequest(Guid LabelId);

// User requests
public record UpdateUserRequest(string? Name, UserRole? Role);

// Lane requests
public record UpdateLaneRequest(string? Name, int? Position);

// Size requests
public record UpdateSizeRequest(string? Name, int? Ordinal);

// Archive requests
public record RestoreCardRequest(Guid LaneId);

// Prune requests
public record PruneRequest(DateTimeOffset? OlderThan, Guid[]? LaneIds, Guid[]? LabelIds);
