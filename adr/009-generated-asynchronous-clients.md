# ADR 009 — Generated Asynchronous DurableObject Clients

Status: Accepted
Date: 2026-07-12
Related plan: `plans/005-generated-async-clients.md`
Related issue: https://github.com/temporal-community/durable-objects-dotnet/issues/8

## Context

DurableObject contracts expose synchronous Temporal queries because workflow query handlers cannot
return `Task`. Using the same contract as a client therefore parks a thread for query RPCs.
`DispatchProxy` also depends on runtime code generation and cannot be used by NativeAOT.

Changing existing query signatures would break workflow implementations and callers. Generated
clients must coexist with existing contracts and with applications that do not install the
analyzer package.

## Decision

`TemporalCommunity.DurableObjects.Analyzers` generates a public concrete client for each supported
DurableObject contract.

- The concrete client implements the original interface. Updates dispatch directly through
  `DurableObjectClientInvoker`; synchronous queries remain explicit compatibility members.
- Each query gains a public `Task<TResult> MethodAsync(...)` method.
- Update and generated query methods gain overloads accepting `DurableObjectCallOptions`.
- A generated `Get{Contract}Client` extension returns the concrete type.
- A generated module initializer registers a construction delegate. `IDurableObjectFactory.Get<T>`
  prefers that delegate and otherwise falls back to `DispatchProxy`.
- `DO0005` rejects shapes that cannot be generated safely. Initial support is intentionally
  limited to public, top-level, non-generic contracts without generic, ref-like, or dynamic
  handlers or generated-name collisions.

The registry and invoker contain no reflection dispatch. NativeAOT verification publishes and
executes an application that observes the generated module registration and concrete client type.

## Consequences

Existing source and wire contracts remain valid. Applications opt into generation by installing
the analyzer package; they can migrate query call sites independently. `DispatchProxy` remains a
compatibility mechanism and is not removed by this decision. NativeAOT support applies to the
generated client-dispatch path, not automatically to reflection-based worker discovery or every
Temporal SDK feature.
