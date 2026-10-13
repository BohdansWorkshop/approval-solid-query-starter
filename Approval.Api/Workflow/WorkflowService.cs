using System.Collections.Concurrent;

namespace Approval.Api.Workflow;

// This starter deliberately keeps storage, validation, workflow and time in one owner.
// The candidate's task is to separate the stated responsibilities without changing behavior.
public sealed class WorkflowService
{
    private readonly ConcurrentDictionary<Guid, TaskState> _tasks = new();
    private readonly object _gate = new();

    public OperationResult<TaskSnapshot> Create(CreateTaskRequest request)
    {
        lock (_gate)
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.DocumentNumber) ||
                string.IsNullOrWhiteSpace(request.Name) ||
                string.IsNullOrWhiteSpace(request.AssigneeId))
            {
                return new(400, null, "DocumentNumber, Name and AssigneeId are required.");
            }

            var now = DateTime.UtcNow;
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

            _tasks[task.Id] = task;
            return new(201, Snapshot(task), null);
        }
    }

    public OperationResult<TaskSnapshot> Get(Guid id)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(id, out var task))
            {
                return new(404, null, "Task not found.");
            }

            return new(200, Snapshot(task), null);
        }
    }

    public OperationResult<TaskSnapshot> Execute(Guid id, ActionRequest request)
    {
        lock (_gate)
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
            var comment = string.IsNullOrWhiteSpace(request.Comment)
                ? null
                : request.Comment.Trim();

            if (!_tasks.TryGetValue(id, out var task))
            {
                return new(404, null, "Task not found.");
            }

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

            if (action == ApprovalAction.Reject && comment is null)
            {
                return new(400, null, "Comment is required for Reject.");
            }

            var now = DateTime.UtcNow;
            task.Status = nextStatus.Value;
            task.UpdatedAtUtc = now;
            task.History.Add(new HistorySnapshot(action, actorId, comment, now));

            return new(200, Snapshot(task), null);
        }
    }

    private static TaskSnapshot Snapshot(TaskState task) => new(
        task.Id,
        task.DocumentNumber,
        task.Name,
        task.AssigneeId,
        task.Status,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.History.ToArray());
}
