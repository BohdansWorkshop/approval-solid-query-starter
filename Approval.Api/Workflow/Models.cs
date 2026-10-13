namespace Approval.Api.Workflow;

public enum ApprovalStatus { Assigned, InProgress, Approved, Rejected }
public enum ApprovalAction { Start, Approve, Reject }

public sealed record CreateTaskRequest(string? DocumentNumber, string? Name, string? AssigneeId);
public sealed record ActionRequest(string? ActorId, string? Action, string? Comment);
public sealed record HistorySnapshot(ApprovalAction Action, string ActorId, string? Comment, DateTime AtUtc);
public sealed record TaskSnapshot(Guid Id, string DocumentNumber, string Name, string AssigneeId,
    ApprovalStatus Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<HistorySnapshot> History);
public sealed record OperationResult<T>(int StatusCode, T? Value, string? Error) where T : class;

public sealed class TaskState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DocumentNumber { get; init; } = "";
    public string Name { get; init; } = "";
    public string AssigneeId { get; init; } = "";
    public ApprovalStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<HistorySnapshot> History { get; init; } = [];
}
