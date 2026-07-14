# ADR 010 — Object Identity and Visibility Metadata

Status: Accepted
Date: 2026-07-12
Related plan: `plans/007-object-identity-and-visibility.md`
Related issue: https://github.com/temporal-community/temporal-dotnet-extensions/issues/9

## Context

Temporal workflow IDs are namespace-wide, while applications often think of actor IDs as scoped
to a type or task queue. Rewriting IDs inside the library would break existing histories and make
interop with ordinary Temporal clients surprising.

The existing visibility API returns only IDs and includes schedule-created one-shot executions.
Distinguishing those executions by their timestamp suffix is fragile. Temporal already records the
originating schedule in the built-in `TemporalScheduledById` Search Attribute.

## Decision

- Caller-supplied object IDs remain exact Temporal workflow IDs. The library does not add type or
  task-queue prefixes.
- Applications own their namespace-wide ID convention.
- `ListDurableObjectExecutionsAsync<T>` returns immutable execution metadata and excludes
  schedule-created executions by default.
- `DurableObjectListOptions.IncludeScheduled` includes them and exposes their `ScheduleId`.
- The existing `ListDurableObjectsAsync<T>` ID stream retains its original inclusion behavior.
- Both visibility APIs remain unavailable in the `netstandard2.1` asset.

## Consequences

Existing workflow histories and callers are unaffected. Canonical filtering requires no custom
Search Attribute registration. All listing results remain eventually consistent because they are
served by Temporal Visibility.
