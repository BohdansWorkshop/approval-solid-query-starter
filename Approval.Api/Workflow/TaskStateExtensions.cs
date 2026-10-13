namespace Approval.Api.Workflow;

internal static class TaskStateExtensions
{
    public static TaskSnapshot ToSnapshot(this TaskState task) => new(
        task.Id,
        task.DocumentNumber,
        task.Name,
        task.AssigneeId,
        task.Status,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.History.ToArray());
}
