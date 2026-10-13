using Approval.Api.Workflow;

namespace Approval.Tests;

public sealed class WorkflowDependencyTests
{
    [Fact]
    public void WorkflowUsesInjectedStoreAndTimeProviderForApproveAndRejectPaths()
    {
        var time = new ControlledTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var workflow = new WorkflowService(new FakeApprovalTaskStore(), time);

        var approveCreated = workflow.Create(new CreateTaskRequest(" DOC-A ", " Approve me ", " user-1 ")).Value!;
        Assert.Equal(time.UtcDateTime, approveCreated.CreatedAtUtc);
        Assert.Equal(time.UtcDateTime, approveCreated.UpdatedAtUtc);
        Assert.Empty(approveCreated.History);

        time.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 5, 0, TimeSpan.Zero);
        var approveStarted = workflow.Execute(approveCreated.Id, new ActionRequest("user-1", "Start", null)).Value!;
        Assert.Equal(ApprovalStatus.InProgress, approveStarted.Status);
        Assert.Equal(time.UtcDateTime, approveStarted.UpdatedAtUtc);
        Assert.Single(approveStarted.History);
        AssertHistory(approveStarted.History[0], ApprovalAction.Start, "user-1", null, time.UtcDateTime);

        time.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 6, 0, TimeSpan.Zero);
        var approved = workflow.Execute(approveCreated.Id, new ActionRequest("user-1", "Approve", " done ")).Value!;
        Assert.Equal(ApprovalStatus.Approved, approved.Status);
        Assert.Equal(time.UtcDateTime, approved.UpdatedAtUtc);
        Assert.Equal(2, approved.History.Count);
        AssertHistory(approved.History[0], ApprovalAction.Start, "user-1", null, new DateTime(2026, 1, 2, 3, 5, 0, DateTimeKind.Utc));
        AssertHistory(approved.History[1], ApprovalAction.Approve, "user-1", "done", time.UtcDateTime);

        time.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 7, 0, TimeSpan.Zero);
        var rejectCreated = workflow.Create(new CreateTaskRequest("DOC-R", "Reject me", "user-2")).Value!;
        Assert.Equal(time.UtcDateTime, rejectCreated.CreatedAtUtc);
        Assert.Equal(time.UtcDateTime, rejectCreated.UpdatedAtUtc);

        time.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 8, 0, TimeSpan.Zero);
        workflow.Execute(rejectCreated.Id, new ActionRequest("user-2", "Start", null));

        time.UtcNow = new DateTimeOffset(2026, 1, 2, 3, 9, 0, TimeSpan.Zero);
        var rejected = workflow.Execute(rejectCreated.Id, new ActionRequest("user-2", "Reject", " needs work ")).Value!;
        Assert.Equal(ApprovalStatus.Rejected, rejected.Status);
        Assert.Equal(time.UtcDateTime, rejected.UpdatedAtUtc);
        Assert.Equal(2, rejected.History.Count);
        AssertHistory(rejected.History[0], ApprovalAction.Start, "user-2", null, new DateTime(2026, 1, 2, 3, 8, 0, DateTimeKind.Utc));
        AssertHistory(rejected.History[1], ApprovalAction.Reject, "user-2", "needs work", time.UtcDateTime);
    }

    [Fact]
    public void FailedActionThroughInjectedDependenciesLeavesStoredStateUnchanged()
    {
        var time = new ControlledTimeProvider(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        var workflow = new WorkflowService(new FakeApprovalTaskStore(), time);
        var created = workflow.Create(new CreateTaskRequest("DOC-1", "Document", "user-1")).Value!;

        time.UtcNow = new DateTimeOffset(2026, 2, 3, 4, 6, 0, TimeSpan.Zero);
        var result = workflow.Execute(created.Id, new ActionRequest("user-2", "Start", null));
        var stored = workflow.Get(created.Id).Value!;

        Assert.Equal(403, result.StatusCode);
        Assert.Equal(created.Status, stored.Status);
        Assert.Equal(created.UpdatedAtUtc, stored.UpdatedAtUtc);
        Assert.Empty(stored.History);
    }

    private static void AssertHistory(
        HistorySnapshot actual,
        ApprovalAction action,
        string actorId,
        string? comment,
        DateTime atUtc)
    {
        Assert.Equal(action, actual.Action);
        Assert.Equal(actorId, actual.ActorId);
        Assert.Equal(comment, actual.Comment);
        Assert.Equal(atUtc, actual.AtUtc);
    }

    private sealed class ControlledTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public DateTime UtcDateTime => UtcNow.UtcDateTime;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeApprovalTaskStore : IApprovalTaskStore
    {
        private readonly Dictionary<Guid, TaskState> _tasks = [];
        private readonly object _gate = new();

        public TaskSnapshot Add(TaskState task)
        {
            lock (_gate)
            {
                _tasks.Add(task.Id, task);
                return Snapshot(task);
            }
        }

        public TaskSnapshot? Get(Guid id)
        {
            lock (_gate)
            {
                return _tasks.TryGetValue(id, out var task) ? Snapshot(task) : null;
            }
        }

        public TResult? Update<TResult>(Guid id, Func<TaskState, TResult> update) where TResult : class
        {
            lock (_gate)
            {
                return _tasks.TryGetValue(id, out var task) ? update(task) : null;
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
}
