# Plan 007: Object Identity and Visibility

Status: Complete
Depends on: Plan 002
GitHub issue: [#9](https://github.com/temporal-community/temporal-dotnet-extensions/issues/9)
Completed by: [a5d841e](https://github.com/temporal-community/temporal-dotnet-extensions/commit/a5d841e)

## Outcome

Make namespace-wide identity and visibility results explicit without rewriting caller-supplied
workflow IDs or requiring custom Temporal Search Attributes.

## Decisions

- A DurableObject ID remains the exact Temporal workflow ID supplied by the caller.
- Identity is namespace-wide; applications own any type-prefix convention they require.
- Add a rich execution descriptor alongside the existing string-only listing API.
- Classify schedule-created executions with Temporal's built-in `TemporalScheduledById` Search
  Attribute rather than parsing time-suffixed IDs.
- Preserve the existing `ListDurableObjectsAsync` behavior for compatibility.

## Implementation

- [x] Add immutable list options and execution metadata types.
- [x] Add a rich async listing API on .NET 8+ with canonical/scheduled filtering.
- [x] Keep the existing ID stream source compatible and behavior compatible.
- [x] Replace ID-suffix filtering guidance with built-in schedule metadata guidance.

## Tests

Test visibility query construction, metadata mapping, schedule classification, compatibility of the
existing ID stream, and the .NET Standard unsupported path. Use integration coverage only where
server visibility behavior is material.

## Documentation

Document namespace-wide IDs, canonical listing defaults, scheduled execution inclusion, and
eventual consistency. Do not prescribe an application-specific ID format.

## Completion criteria

- Callers can enumerate rich execution metadata without dropping to the Temporal SDK.
- Scheduled executions can be included or excluded without ID heuristics.
- No custom Search Attribute registration is required.
- Existing IDs and listing callers remain compatible.
