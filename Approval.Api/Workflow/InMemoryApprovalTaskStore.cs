using System.Collections.Concurrent;

namespace Approval.Api.Workflow;

public sealed class InMemoryApprovalTaskStore : IApprovalTaskStore
{
    private readonly ConcurrentDictionary<Guid, TaskState> _tasks = new();
    private readonly Lock _gate = new();

    public TaskSnapshot Add(TaskState task)
    {
        lock (_gate)
        {
            _tasks[task.Id] = task;
            return task.ToSnapshot();
        }
    }

    public TaskSnapshot? Get(Guid id)
    {
        lock (_gate)
        {
            return _tasks.TryGetValue(id, out var task)
                ? task.ToSnapshot()
                : null;
        }
    }

    public TResult? Update<TResult>(Guid id, Func<TaskState, TResult> update) where TResult : class
    {
        lock (_gate)
        {
            return _tasks.TryGetValue(id, out var task)
                ? update(task)
                : null;
        }
    }
}
