namespace Approval.Api.Workflow;

public interface IApprovalTaskStore
{
    TaskSnapshot Add(TaskState task);
    TaskSnapshot? Get(Guid id);
    TResult? Update<TResult>(Guid id, Func<TaskState, TResult> update) where TResult : class;
}
