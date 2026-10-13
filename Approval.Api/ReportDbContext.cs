using Approval.Api.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Approval.Api;

// Isolated SQL exercise: these fixtures are not a mirror of the in-memory workflow.
public sealed class ReportDbContext(DbContextOptions<ReportDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRow> Documents => Set<DocumentRow>();
    public DbSet<DocumentEventRow> DocumentEvents => Set<DocumentEventRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DocumentRow>().HasKey(x => x.Id);
        model.Entity<DocumentRow>().Property(x => x.Number).IsRequired();
        model.Entity<DocumentRow>().Property(x => x.Name).IsRequired();
        model.Entity<DocumentRow>().Property(x => x.AssigneeId).IsRequired();
        model.Entity<DocumentRow>().Property(x => x.Payload).IsRequired();
        // The index matches the query’s filter and sort order, so the database can read just the requested page straight from the index, without scanning or sorting
        model.Entity<DocumentRow>()
            .HasIndex(x => new { x.AssigneeId, x.Status, x.CreatedAtUtc, x.Id })
            .IsDescending(false, false, true, false);
        model.Entity<DocumentRow>().HasMany(x => x.Events).WithOne(x => x.Document)
            .HasForeignKey(x => x.DocumentId);
        model.Entity<DocumentEventRow>().HasKey(x => x.Id);
    }
}

public sealed class DocumentRow
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string AssigneeId { get; set; } = "";
    public ApprovalStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Payload { get; set; } = "";
    public List<DocumentEventRow> Events { get; set; } = [];
}

public sealed class DocumentEventRow
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public DocumentRow Document { get; set; } = null!;
    public string Action { get; set; } = "";
    public DateTime AtUtc { get; set; }
}
