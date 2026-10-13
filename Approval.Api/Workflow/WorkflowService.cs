namespace Approval.Api.Workflow;

public sealed class WorkflowService
{
    private readonly IApprovalTaskStore _store;
    private readonly TimeProvider _timeProvider;

    public WorkflowService(IApprovalTaskStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public OperationResult<TaskSnapshot> Create(CreateTaskRequest? request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.DocumentNumber) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.AssigneeId))
        {
            return new(400, null, "DocumentNumber, Name and AssigneeId are required.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var task = new TaskState
        {
            Id = Guid.NewGuid(),
            DocumentNumber = request.DocumentNumber.Trim(),
            Name = request.Name.Trim(),
            AssigneeId = request.AssigneeId.Trim(),
            Status = ApprovalStatus.Assigned,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        return new(201, _store.Add(task), null);
    }

    public OperationResult<TaskSnapshot> Get(Guid id)
    {
        var task = _store.Get(id);
        return task is null
            ? new(404, null, "Task not found.")
            : new(200, task, null);
    }

    public OperationResult<TaskSnapshot> Execute(Guid id, ActionRequest? request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ActorId) ||
            string.IsNullOrWhiteSpace(request.Action))
        {
            return new(400, null, "ActorId and Action are required.");
        }

        var actionText = request.Action.Trim();
        ApprovalAction? parsedAction = actionText.ToUpperInvariant() switch
        {
            "START" => ApprovalAction.Start,
            "APPROVE" => ApprovalAction.Approve,
            "REJECT" => ApprovalAction.Reject,
            _ => null
        };

        if (parsedAction is null)
        {
            return new(400, null, "Action must be Start, Approve or Reject.");
        }

        var action = parsedAction.Value;

        var result = _store.Update<OperationResult<TaskSnapshot>>(id, task =>
        {
            var actorId = request.ActorId.Trim();
            if (!string.Equals(task.AssigneeId, actorId, StringComparison.Ordinal))
            {
                return new(403, null, "Only the assignee may execute an action.");
            }

            ApprovalStatus? nextStatus = (task.Status, action) switch
            {
                (ApprovalStatus.Assigned, ApprovalAction.Start) => ApprovalStatus.InProgress,
                (ApprovalStatus.InProgress, ApprovalAction.Approve) => ApprovalStatus.Approved,
                (ApprovalStatus.InProgress, ApprovalAction.Reject) => ApprovalStatus.Rejected,
                _ => null
            };

            if (nextStatus is null)
            {
                return new(409, null, "Action is not allowed in the current status.");
            }

            var comment = string.IsNullOrWhiteSpace(request.Comment)
                ? null
                : request.Comment.Trim();

            if (action == ApprovalAction.Reject && comment is null)
            {
                return new(400, null, "Comment is required for Reject.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            task.Status = nextStatus.Value;
            task.UpdatedAtUtc = now;
            task.History.Add(new HistorySnapshot(action, actorId, comment, now));

            return new(200, task.ToSnapshot(), null);
        });

        return result ?? new(404, null, "Task not found.");
    }
}
