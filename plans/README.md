# Engineering Plans

This directory contains maintainer-facing implementation plans. User documentation belongs in
`docs/`; lasting architectural decisions belong in `adr/`.

Plans describe intended work, not shipped behavior. GitHub issues track live status, and pull
requests should link the plan phase they implement. Keep plans concise, update them when a design
decision changes, and remove completed checklists that no longer aid maintenance.

## Status

Use one of these values at the top of every plan:

| Status | Meaning |
|---|---|
| Proposed | Written but not approved for implementation. |
| Approved | Direction accepted and ready for implementation. |
| In Progress | At least one implementation issue is active. |
| Blocked | Work cannot proceed without a named decision or dependency. |
| Complete | Every completion criterion is satisfied. |
| Superseded | Replaced by another plan or decision. |

GitHub is the live source of truth for execution. A plan becomes `Complete` only after its
completion criteria, meaningful tests, necessary documentation, and compatibility checks are done.
The plan should link the issues and pull requests that provide completion evidence.

Each plan should include, when relevant:

- the outcome and current problem;
- concrete implementation work;
- source, binary, serialization, and workflow-history compatibility concerns;
- tests that prove meaningful behavior or prevent credible regressions;
- necessary user documentation and sample changes; and
- objective completion criteria.

Do not add tests that merely mirror implementation details or documentation that restates an API.

## Linking work

- Give each plan one GitHub tracking issue.
- Link implementation checklist items to issues when the work is large enough to review separately.
- When checking an item, link the pull request or commit that completed it.
- Pull requests should identify the epic, plan, issue, compatibility impact, and verification.
- Update the epic progress table when a plan changes status.

## Active initiative

- [Durable actor platform](durable-actor-platform.md)
- [001 — Product positioning](001-product-positioning.md)
- [002 — Correctness guarantees](002-correctness-guarantees.md)
- [003 — Strongly typed durable state](003-typed-durable-state.md)
- [004 — Temporal analyzer platform](004-temporal-analyzer-platform.md)
- [005 — Generated asynchronous clients](005-generated-async-clients.md)
- [006 — DurableObject call context](006-durable-object-call-context.md)
- [007 — Object identity and visibility](007-object-identity-and-visibility.md)
- [008 — Performance, replay, NativeAOT, and scale](008-performance-replay-aot-scale.md)
