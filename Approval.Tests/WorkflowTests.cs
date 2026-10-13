using Approval.Api.Workflow;

namespace Approval.Tests;

public sealed class WorkflowTests
{
    private static WorkflowService NewWorkflow() => new(new InMemoryApprovalTaskStore(), TimeProvider.System);

    private static TaskSnapshot Create(WorkflowService workflow) =>
        workflow.Create(new CreateTaskRequest("DOC-1", "Document", "user-1")).Value!;

    private static void Unchanged(TaskSnapshot before, TaskSnapshot after)
    {
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(before.History.ToArray(), after.History.ToArray());
    }

    [Fact]
    public void CreateAssignsInitialStateAndEqualUtcDates()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(ApprovalStatus.Assigned, task.Status);
        Assert.Equal(task.CreatedAtUtc, task.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, task.CreatedAtUtc.Kind);
        Assert.Empty(task.History);
    }

    [Fact]
    public void MissingCreateRequestReturns400()
    {
        var workflow = NewWorkflow();
        Assert.Equal(400, workflow.Create(null).StatusCode);
    }

    [Theory]
    [InlineData("Approve", ApprovalStatus.Approved)]
    [InlineData("Reject", ApprovalStatus.Rejected)]
    public void FullPathPreservesEarlierSnapshot(string action, ApprovalStatus expected)
    {
        var workflow = NewWorkflow();
        var created = Create(workflow);
        var started = workflow.Execute(created.Id, new ActionRequest("user-1", "Start", null));
        var final = workflow.Execute(created.Id, new ActionRequest("user-1", action, " Decision "));
        Assert.Equal(200, started.StatusCode);
        Assert.Equal(ApprovalStatus.InProgress, started.Value!.Status);
        Assert.Single(started.Value.History);
        Assert.Equal(200, final.StatusCode);
        Assert.Equal(expected, final.Value!.Status);
        Assert.Equal(2, final.Value.History.Count);
        Assert.Equal("Decision", final.Value.History.Last().Comment);
        Assert.Equal(final.Value.UpdatedAtUtc, final.Value.History.Last().AtUtc);
        Assert.Equal(ApprovalStatus.Assigned, created.Status);
        Assert.Empty(created.History);
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    public void CannotFinalizeBeforeStart(string action)
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        var result = workflow.Execute(task.Id, new ActionRequest("user-1", action, "Reason"));
        Assert.Equal(409, result.StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public void OtherActorCannotChangeTask()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        Assert.Equal(403, workflow.Execute(task.Id, new ActionRequest("user-2", "Start", null)).StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectRequiresCommentAndPreservesState(string? comment)
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        var started = workflow.Execute(task.Id, new ActionRequest("user-1", "Start", null)).Value!;
        Assert.Equal(400, workflow.Execute(task.Id, new ActionRequest("user-1", "Reject", comment)).StatusCode);
        Unchanged(started, workflow.Get(task.Id).Value!);
    }

    [Theory]
    [InlineData("Approve", "Start")]
    [InlineData("Approve", "Approve")]
    [InlineData("Approve", "Reject")]
    [InlineData("Reject", "Start")]
    [InlineData("Reject", "Approve")]
    [InlineData("Reject", "Reject")]
    public void TerminalStateCannotChange(string terminal, string action)
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        workflow.Execute(task.Id, new ActionRequest("user-1", "Start", null));
        var final = workflow.Execute(task.Id, new ActionRequest("user-1", terminal, "Reason")).Value!;
        Assert.Equal(409, workflow.Execute(task.Id, new ActionRequest("user-1", action, "Reason")).StatusCode);
        Unchanged(final, workflow.Get(task.Id).Value!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("999")]
    [InlineData("1")]
    [InlineData("Start,Approve")]
    public void UnknownActionCannotChangeTask(string? action)
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        Assert.Equal(400, workflow.Execute(task.Id, new ActionRequest("user-1", action, null)).StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public void MissingTaskReturns404()
    {
        var workflow = NewWorkflow();
        Assert.Equal(404, workflow.Get(Guid.NewGuid()).StatusCode);
        Assert.Equal(404, workflow.Execute(Guid.NewGuid(), new ActionRequest("user-1", "Start", null)).StatusCode);
    }

    [Fact]
    public void MissingActionRequestReturns400BeforeTaskLookup()
    {
        var workflow = NewWorkflow();
        Assert.Equal(400, workflow.Execute(Guid.NewGuid(), null).StatusCode);
    }

    [Fact]
    public void RejectWithoutCommentFromAssignedReturns409BeforeCommentCheck()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);

        var result = workflow.Execute(task.Id, new ActionRequest("user-1", "Reject", null));

        Assert.Equal(409, result.StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public void RejectWithoutCommentFromFinalStatusReturns409BeforeCommentCheck()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        workflow.Execute(task.Id, new ActionRequest("user-1", "Start", null));
        var approved = workflow.Execute(task.Id, new ActionRequest("user-1", "Approve", null)).Value!;

        var result = workflow.Execute(task.Id, new ActionRequest("user-1", "Reject", null));

        Assert.Equal(409, result.StatusCode);
        Unchanged(approved, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public void RejectWithoutCommentByOtherActorReturns403BeforeCommentCheck()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);

        var result = workflow.Execute(task.Id, new ActionRequest("user-2", "Reject", null));

        Assert.Equal(403, result.StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public void RejectWithoutCommentForMissingTaskReturns404BeforeCommentCheck()
    {
        var workflow = NewWorkflow();

        var result = workflow.Execute(Guid.NewGuid(), new ActionRequest("user-1", "Reject", null));

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void UnknownActionForMissingTaskReturns400BeforeTaskLookup()
    {
        var workflow = NewWorkflow();

        var result = workflow.Execute(Guid.NewGuid(), new ActionRequest("user-1", "Unknown", null));

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void OtherActorIllegalTransitionReturns403BeforeTransitionCheck()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);

        var result = workflow.Execute(task.Id, new ActionRequest("user-2", "Approve", null));

        Assert.Equal(403, result.StatusCode);
        Unchanged(task, workflow.Get(task.Id).Value!);
    }

    [Fact]
    public async Task TwoConcurrentFinalActionsAllowExactlyOneDecision()
    {
        var workflow = NewWorkflow();
        var task = Create(workflow);
        workflow.Execute(task.Id, new ActionRequest("user-1", "Start", null));
        using var barrier = new Barrier(2);
        OperationResult<TaskSnapshot> Decide(string action)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return workflow.Execute(task.Id, new ActionRequest("user-1", action, "Reason"));
        }
        var results = await Task.WhenAll(Task.Run(() => Decide("Approve")), Task.Run(() => Decide("Reject")));
        var winner = Assert.Single(results, x => x.StatusCode == 200);
        Assert.Single(results, x => x.StatusCode == 409);
        var stored = workflow.Get(task.Id).Value!;
        Assert.Equal(winner.Value!.Status, stored.Status);
        Assert.Equal(2, stored.History.Count);
        Assert.Single(stored.History, x => x.Action is ApprovalAction.Approve or ApprovalAction.Reject);
    }
}
