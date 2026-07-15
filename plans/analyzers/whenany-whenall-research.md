# Task.WhenAny and Task.WhenAll research

Reviewed against the local Temporal .NET SDK source and workflow tests.

## Decision

Implement TEMP005 as a warning for only the higher-risk generic `Task.WhenAny<TResult>` shapes:

- the generic enumerable overload; and
- the generic params overload with more than two direct result-task arguments.

The Temporal wrapper is the portable replacement because older target frameworks did not always
schedule generic `Task.WhenAny` correctly. Two generic result tasks and all non-generic overloads
remain unreported because the SDK tests treat them as safe.

The code fix replaces `Task.WhenAny` with `Workflow.WhenAnyAsync` while preserving arguments.

Do not implement TEMP006 yet. The SDK's `Workflow.WhenAllAsync` methods currently delegate to
`Task.WhenAll` because the standard overloads are deterministic. The wrapper is a defensive
convention, not a concrete correctness violation.

## Deliberate limits

- No target-framework inference is attempted; the warning recommends the portable wrapper rather
  than claiming every flagged call is nondeterministic.
- A params-array variable with an unknown element count is not flagged.
- `Task.WhenAny` calls outside workflow types are not analyzed.
- `Task.WhenAll` remains documentation-only.
