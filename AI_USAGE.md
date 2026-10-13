## CLAUDE 

Claude was used to turn my manually written technical task into a structured prompt for Codex. Codex then performed the implementation.

## CODEX query

# Context

Read `AGENTS.md` first. All rules there (API contracts, state transitions, validation order, atomicity, concurrency) are hard invariants. Do not change any observable behavior of the approval workflow while doing the tasks below. Existing tests must keep passing.

Work on the two tasks in order. Complete and verify Task 1 before starting Task 2. Keep changes minimal and focused; do not refactor unrelated code.

---

# Task 1 — Practical SOLID: separate responsibilities in `WorkflowService`

## Problem
`WorkflowService` currently mixes three concerns: business rules, data storage, and obtaining system time.

## Requirements
1. **Extract storage (SRP, DIP).**
   - Introduce a storage abstraction (interface shape is up to you; keep it small and driven by what the workflow actually needs).
   - Move all persistence code out of `WorkflowService` into an implementation of that abstraction.
   - `WorkflowService` must depend only on the abstraction, never on a concrete storage class.
2. **Replace direct time access (DIP).**
   - Inject `TimeProvider` (or an equivalent replaceable time source) via the constructor.
   - Remove every direct use of `DateTime.UtcNow` / `DateTimeOffset.UtcNow` from the workflow logic.
   - Preserve the invariant: on creation, `CreatedAtUtc == UpdatedAtUtc` (taken from one single time reading).
3. **Wire up dependency injection.** Register the abstraction, its implementation, and the time provider in the DI container. Dependencies must be passed through constructors.
4. **Preserve invariants from `AGENTS.md`:**
   - Validation order stays exactly: 400 (fields/action) → 404 → 403 → 409 → 400 (reject comment).
   - Success atomically updates status, `UpdatedAtUtc`, and history. Failure changes nothing.
   - Two concurrent final actions: exactly one succeeds, the other gets 409, history contains one final decision.
   - The storage abstraction must make this possible (e.g. atomic read-check-write per task or optimistic concurrency). Do not introduce a check-then-act gap by splitting "load" and "save" carelessly.
5. **Do not over-engineer.** The number of interfaces or layers is not a quality criterion. Do not artificially apply all five SOLID principles; apply others (OCP, LSP, ISP) only where they solve a real problem here.

## Tests
Add a **behavioral unit test** that uses:
- a test (in-memory/fake) implementation of the storage abstraction, and
- a controlled time source (e.g. `FakeTimeProvider` or a hand-written fake).

The test must run a workflow (create → `Start` → `Approve`, and ideally a `Reject` path with a comment) and assert:
- the resulting status,
- `CreatedAtUtc` / `UpdatedAtUtc` values (including that they equal the controlled times at each step),
- the history entries (count, order, action, actor, comment, timestamp).

Optionally add a small test that a failed action (e.g. wrong actor) leaves state, `UpdatedAtUtc`, and history unchanged.

## README
Add a short section explaining the **concrete** applications of:
- **SRP:** which responsibilities were separated and where they now live.
- **DIP:** what `WorkflowService` now depends on (abstractions) and what was injected.
Keep it brief and specific to this codebase. Do not write generic SOLID theory.

## Done when
- `WorkflowService` has no direct dependency on concrete storage or on the system clock.
- DI is configured; the app still runs.
- New behavioral test passes; all existing tests pass.
- README section is added.

---

# Task 2 — Practical query optimization: `InboxQuery.ListAsync`

## Goal
Optimize `InboxQuery.ListAsync` while **keeping its signature, DTOs, and results identical**. You are given the schema, 1000 documents with history, and a SQL command counter.

## Query contract (must be preserved)
- **Filters:** `AssigneeId` and `Status`.
- **Ordering:** `CreatedAtUtc DESC, Id ASC`.
- **Paging:** page is 1-based; `pageSize` is between 1 and 100; an empty page is valid.
- **Response fields:** `Id`, `Number`, `Name`, `Status`, `CreatedAtUtc`, `HistoryCount`.

## Requirements
1. **Everything runs in the database:** filtering, sorting, paging, and history counting.
2. **Exactly one SELECT per page.** No separate query per row (remove any N+1 behavior).
3. **Project directly to the response DTO** in the query (`Select(...)`).
   - Do not load `Payload`.
   - Do not load full entities.
   - Do not load history collections; compute `HistoryCount` in SQL (e.g. a correlated count / `Count()` inside the projection).
4. **Do not change** the method signature, the DTOs, result ordering, or paging semantics. Handle validation of `page` / `pageSize` exactly as the existing code does; do not silently change it.
5. **Add an index in `ReportDbContext.OnModelCreating`.**
   - Choose the column order deliberately. Equality-filter columns first, then the sort columns in sort order, so the index serves both filtering and ordering (expected shape: `AssigneeId`, `Status`, `CreatedAtUtc DESC`, `Id`; verify against the actual filter logic, including whether filters are optional).
   - Make the index sort direction match the query ordering if the provider supports it.
   - Justify the column order and the maintenance cost of the index (extra write cost and storage on insert/update, and why it is acceptable here).
   - No migration is needed: tests create a fresh SQLite database.

## Tests
- Remove the `Skip` from the test `PageUsesOneSelectAndDoesNotLoadPayload` and make it pass.
- Make sure result correctness is covered: filters, ordering (including the `Id ASC` tie-breaker), paging (first, middle, last, and empty page), and `HistoryCount` values.
- Do not add benchmarks, `EXPLAIN` output, or millisecond timing measurements.

## README
Add a section that includes:
- The number of SELECT commands **before** and **after** the change, taken from `SqlCommandCounter.Commands`.
- The SQL **after** the change (via `SqlCommandCounter.Commands` or `ToQueryString()`).
- A short justification of the index column order and its maintenance cost.
Keep it concise. No benchmarks, no `EXPLAIN`, no timing numbers.

## Done when
- One SELECT per page, verified by the previously skipped test.
- No `Payload`, full entities, or history collections are loaded.
- Results are identical to the original implementation for the same inputs.
- Index is defined in `OnModelCreating` with justification in the README.
- All tests pass.

---

# Final checklist (verify before finishing)

- [ ] `AGENTS.md` invariants are intact (contracts, transitions, validation order, atomicity, concurrency).
- [ ] No direct use of the system clock or concrete storage inside `WorkflowService`.
- [ ] New behavioral unit test with fake storage and controlled time passes.
- [ ] `PageUsesOneSelectAndDoesNotLoadPayload` is unskipped and passes.
- [ ] README contains: SRP/DIP explanation, SELECT count before/after, SQL after, index justification.
- [ ] Entire test suite is green.

In your final message, summarize what you changed per task, list the files touched, and state any assumptions you made.