using Approval.Api.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Approval.Api.Queries;

public sealed record InboxItem(long Id, string Number, string Name, ApprovalStatus Status,
    DateTime CreatedAtUtc, int HistoryCount);

public sealed class InboxQuery
{
    public async Task<IReadOnlyList<InboxItem>> ListAsync(ReportDbContext db, string assigneeId,
        ApprovalStatus status, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assigneeId);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (page < 1 || page > 10000) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        return await db.Documents.AsNoTracking()
            .Where(x => x.AssigneeId == assigneeId && x.Status == status)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new InboxItem(
                x.Id,
                x.Number,
                x.Name,
                x.Status,
                x.CreatedAtUtc,
                x.Events.Count))
            .ToListAsync(cancellationToken);
    }
}
