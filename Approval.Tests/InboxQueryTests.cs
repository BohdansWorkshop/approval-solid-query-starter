using Approval.Api.Queries;
using Approval.Api.Workflow;

namespace Approval.Tests;

public sealed class InboxQueryTests
{
    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 5)]
    [InlineData(999, 10)]
    public async Task FilteringOrderingPagingAndHistoryCountAreCorrect(int page, int pageSize)
    {
        using var fixture = new ReportFixture();
        var items = await new InboxQuery().ListAsync(fixture.Db, "user-1", ApprovalStatus.InProgress, page, pageSize);
        var expected = fixture.Seeded.Where(x => x.AssigneeId == "user-1" && x.Status == ApprovalStatus.InProgress)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        Assert.Equal(expected.Select(x => x.Id), items.Select(x => x.Id));
        Assert.All(items, x => Assert.Equal(1, x.HistoryCount));
        Assert.All(items, x => Assert.Equal(ApprovalStatus.InProgress, x.Status));
    }

    [Fact]
    public async Task DifferentStatusAndAssigneeAreNotMixed()
    {
        using var fixture = new ReportFixture();
        var items = await new InboxQuery().ListAsync(fixture.Db, "user-3", ApprovalStatus.Rejected, 1, 10);
        Assert.Equal(10, items.Count);
        Assert.All(items, x => Assert.Equal(3, x.HistoryCount));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPageArgumentsAreRejected(int page, int pageSize)
    {
        using var fixture = new ReportFixture();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new InboxQuery().ListAsync(fixture.Db, "user-1", ApprovalStatus.InProgress, page, pageSize));
        Assert.Empty(fixture.Counter.Commands);
    }

    [Fact(Skip = "Candidate: optimize ListAsync, then remove Skip. The supplied version deliberately executes 21 SELECTs.")]
    public async Task PageUsesOneSelectAndDoesNotLoadPayload()
    {
        using var fixture = new ReportFixture();
        var items = await new InboxQuery().ListAsync(fixture.Db, "user-1", ApprovalStatus.InProgress, 1, 20);
        Assert.Equal(20, items.Count);
        var sql = Assert.Single(fixture.Counter.Commands);
        Assert.DoesNotContain("Payload", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
