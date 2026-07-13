# Plan 006: DurableObject Call Context

Status: Proposed
Depends on: None
GitHub issue: [#7](https://github.com/temporal-community/durable-objects-dotnet/issues/7)
Completed by: —

## Outcome

Define client-side cancellation, headers, RPC options, and timeouts without adding transport
concerns to workflow arguments.

## Proposed changes

- Introduce one immutable call-options model for generated and runtime clients.
- Separate RPC cancellation from workflow/update cancellation semantics.
- Define header propagation for authorization and tracing.
- Decide defaults and per-call override behavior.
- Ensure generated query and update methods expose the same capabilities.

## Compatibility

Existing proxy methods remain valid. New options must not alter workflow argument serialization or
wire handler names.

## Implementation

- [ ] Inventory Temporal client update/query RPC option surfaces.
- [ ] Specify the public options API and precedence rules.
- [ ] Prototype update-with-start and query propagation.
- [ ] Record the accepted public contract in an ADR.
- [ ] Add runtime support before generated clients consume it.

## Tests

Test observable cancellation, headers, timeouts, default behavior, and non-mutation of caller-owned
options. Do not test Temporal option properties independently of library propagation.

## Documentation

Document cancellation semantics and one authenticated-call example when implemented.

## Completion criteria

- Update and query calls share one coherent options model.
- Cancellation and headers reach the intended Temporal RPCs.
- Workflow arguments and existing contracts remain compatible.
