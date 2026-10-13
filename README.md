# Changes

## Practical SOLID

SRP: approval business rules and validation order live in `Approval.Api/Workflow/WorkflowService.cs`. Storage state and atomic locking live in `Approval.Api/Workflow/InMemoryApprovalTaskStore.cs`. Snapshot conversion lives in `Approval.Api/Workflow/TaskStateExtensions.cs`. System time is supplied through `TimeProvider`, so the workflow does not read the system clock directly.

DIP: `WorkflowService` depends on `IApprovalTaskStore` and `TimeProvider`, both injected through its constructor. `Program.cs` registers `TimeProvider.System`, `IApprovalTaskStore`, `InMemoryApprovalTaskStore`, and `WorkflowService`. The workflow dependency tests use a fake store and a controlled time source.

Concurrency is preserved by running the read-check-write path inside `IApprovalTaskStore.Update`. The current in-memory implementation serializes updates with its `Lock`, so a losing concurrent final action re-reads the already-final state inside the critical section and returns `409` without overwriting the winning result.

## Inbox query optimization

For `ListAsync(db, "user-1", ApprovalStatus.InProgress, page: 1, pageSize: 20)`, the original implementation from commit `b8ea4c1` executed 21 SELECT commands: one query loading documents and one history-count query for each of the 20 page rows. The optimized implementation executes 1 SELECT for the same filters and page size. These counts were measured from the command counter output.

SQL after the change, copied from the command counter output:

```sql
SELECT "d"."Id", "d"."Number", "d"."Name", "d"."Status", "d"."CreatedAtUtc", (
    SELECT COUNT(*)
    FROM "DocumentEvents" AS "d0"
    WHERE "d"."Id" = "d0"."DocumentId")
FROM "Documents" AS "d"
WHERE "d"."AssigneeId" = @assigneeId AND "d"."Status" = @status
ORDER BY "d"."CreatedAtUtc" DESC, "d"."Id"
LIMIT @p3 OFFSET @p
```

The `Documents` index is `(AssigneeId, Status, CreatedAtUtc DESC, Id)`. `AssigneeId` and `Status` come first because they are equality filters in every inbox query; `CreatedAtUtc DESC` and `Id` match the required ordering and tie-breaker. The tradeoff is extra storage plus index maintenance on inserts and updates to indexed columns, which is acceptable for this read path because every inbox page uses the same filter and sort shape.

# Manual changes:
- Manual changes: codex missed "request is null" check is always false, so i got rid of it. 
- Changed lock's object type for Lock.