# AGENTS.md — AI Assistant Rules: Approval Workflow

> This document defines **mandatory invariants**. Do not change, weaken, "optimize", or reorder them, even if the user asks to "simplify" or it seems more logical. If a request conflicts with these rules, stop, point out the conflict, and do not silently change the behavior.

---

## 0. General Working Principles

1. Before any change to code, tests, or documentation, verify it does not violate sections 1–5.
2. Do not change API contracts (paths, field names, status codes, response shape) without an explicit instruction.
3. Do not change the **validation order** (section 3). This is the most important rule.
4. If a change touches validation order, statuses, atomicity, or concurrency, add or update tests.
5. If a rule is ambiguous, ask instead of guessing.

---

## 1. API Contracts

### `POST /api/approval-tasks`
- **Request:** `documentNumber`, `name`, `assigneeId` (all required, non-empty).
- **Response:** `201 Created` with a task object containing:
  - a generated `ID`;
  - status `Assigned`;
  - `CreatedAtUtc == UpdatedAtUtc` (the **same** UTC value, one and the same timestamp);
  - an empty history collection.

### `POST /api/approval-tasks/{id}/actions`
- **Request:** `actorId` (required), `action` (required: `Start` | `Approve` | `Reject`), `comment` (optional; required for `Reject`).
- **Response:** `200 OK` with the updated task state.

### `GET /api/approval-tasks/{id}`
- **Response:** `200 OK` with a consistent task state (status, dates, fields) and the **full history**.
- If the task does not exist: `404 Not Found`.

---

## 2. Business Rules

### 2.1 Assignee
Only the assigned user may execute an action (`actorId == assigneeId`). Otherwise: `403 Forbidden`.

### 2.2 Allowed Transitions (and only these)

| From status  | Action    | To status    |
|--------------|-----------|--------------|
| `Assigned`   | `Start`   | `InProgress` |
| `InProgress` | `Approve` | `Approved`   |
| `InProgress` | `Reject`  | `Rejected`   |

Any other combination of status + action is an illegal transition → `409 Conflict`.
Examples: `Approve`/`Reject` from `Assigned`; a repeated `Start` from `InProgress`.

### 2.3 Rejection Comment
`Reject` requires a **non-empty** comment. Missing, `null`, `""`, or whitespace-only → `400 Bad Request`.
For `Start` and `Approve` the comment is optional.

### 2.4 Finality
`Approved` and `Rejected` are final statuses. After them, **any** action is prohibited → `409 Conflict`.

### 2.5 Atomicity
- **Success:** status, `UpdatedAtUtc`, and the history entry change **together in a single atomic operation**. Partial updates are not allowed.
- **Failure:** **nothing changes**: not the status, not the timestamp, not the history.
- A validation failure must leave no side effects.

### 2.6 Concurrency
- If two final actions (`Approve`/`Reject`) arrive at the same time for the same task: **exactly one succeeds**, the second receives `409 Conflict`.
- History must contain **exactly one** final decision.
- The implementation must serialize state changes per task (per-task locking or optimistic concurrency, e.g. a version/ETag). The losing request **re-evaluates** the state and gets `409`; it never overwrites the result.
- The status check and the write of the new state must happen in one critical section / transaction (no check-then-act gap).

---

## 3. Validation Order (DO NOT CHANGE)

For `POST /api/approval-tasks/{id}/actions`, checks run **strictly in this order**; the first one that fails returns the error and the later ones are **not** evaluated:

| #  | Check                                                                          | Code  |
|----|--------------------------------------------------------------------------------|-------|
| 1  | Empty/missing required fields (`actorId`, `action`) or missing/unknown action  | `400` |
| 2  | Task with this ID does not exist                                               | `404` |
| 3  | `actorId` ≠ `assigneeId`                                                       | `403` |
| 4  | Action not allowed in the current status (illegal transition or task is final) | `409` |
| 5  | `Reject` without a non-empty comment                                           | `400` |

### Consequences of the order (must be covered by tests)

| Situation                                                        | Result |
|------------------------------------------------------------------|--------|
| `Reject` without comment, task in `Assigned`                     | **409** (step 4 comes before step 5) |
| `Reject` without comment, task `Approved`/`Rejected`             | **409** |
| `Reject` without comment, non-assignee actor                     | **403** |
| `Reject` without comment, task does not exist                    | **404** |
| Unknown action + nonexistent task                                | **400** (step 1 before step 2) |
| Nonexistent task + non-assignee actor                            | **404** |
| Non-assignee actor + illegal transition                          | **403** |
| `Reject` without comment, `InProgress`, correct assignee         | **400** |

> Two checks return `400`, but they are **different steps** (1 and 5). Never merge them into one early validation — that would break the order.

### Forbidden
- Moving the comment check to step 1, or into DTO/model validation that runs before the status check.
- Using automatic framework validation (`[Required]` attributes, FluentValidation filters, etc.) for the "comment required on `Reject`" rule — it fires too early.
- Checking task existence before validating fields/action.
- Checking status before checking the assignee.

---

## 4. Checklist Before Finishing Any Change

- [ ] `POST/POST/GET` contracts unchanged (paths, fields, codes).
- [ ] `201` returns `Assigned`, `CreatedAtUtc == UpdatedAtUtc`, empty history.
- [ ] Validation order 400 → 404 → 403 → 409 → 400 preserved.
- [ ] Only the 3 transitions from section 2.2 are allowed.
- [ ] Final statuses block all subsequent actions (`409`).
- [ ] Success: status + timestamp + history updated atomically.
- [ ] Failure: task state and history unchanged.
- [ ] Concurrent final actions: one `200`, one `409`, one final decision in history.
- [ ] Tests for the consequences table in section 3 exist and pass.

---

## 5. Minimum Required Test Set

1. Task creation: `201`, `Assigned`, equal UTC timestamps, empty history.
2. Full paths `Start → Approve` and `Start → Reject (with comment)`: `200`, history grows by 1 entry per action.
3. Every error in the section 3 table, individually **and** in combinations (to verify precedence).
4. After a failed action: `GET` returns the same state; `UpdatedAtUtc` and history unchanged.
5. Action after `Approved`/`Rejected` → `409`.
6. Parallel `Approve` + `Reject` (or two `Approve`): exactly one `200`, one `409`, one final entry in history.
7. `GET` for a nonexistent task → `404`.

---

## 6. If a Request Conflicts With These Rules

Respond as follows:
1. Name the exact rule (section and item) being violated.
2. Explain the consequence.
3. Offer a compatible alternative.
4. Do not introduce the violation unless the user explicitly confirms that the rules are being changed.
