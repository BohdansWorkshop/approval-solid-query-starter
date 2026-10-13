using Approval.Api;
using Approval.Api.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Approval.Tests;

public sealed class ReportFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public ReportDbContext Db { get; }
    public SqlCommandCounter Counter { get; } = new();
    public IReadOnlyList<DocumentRow> Seeded { get; }

    public ReportFixture()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ReportDbContext>()
            .UseSqlite(_connection).AddInterceptors(Counter).Options;
        Db = new ReportDbContext(options);
        Db.Database.EnsureCreated();
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Seeded = Enumerable.Range(1, 1000).Select(i => new DocumentRow
        {
            Id = i, Number = $"DOC-{i:D4}", Name = $"Document {i}",
            AssigneeId = $"user-{i % 10}", Status = (ApprovalStatus)(i % 4),
            CreatedAtUtc = origin.AddMinutes(i / 100), Payload = new string('x', 2048),
            Events = Enumerable.Range(0, i % 4).Select(j => new DocumentEventRow
            {
                Action = "History", AtUtc = origin.AddMinutes(i / 100).AddSeconds(j)
            }).ToList()
        }).ToArray();
        Db.Documents.AddRange(Seeded);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        Counter.Reset();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
