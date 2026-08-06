# ADR 008 — DurableObject Call Context

Status: Accepted
Date: 2026-07-12
Related issue: https://github.com/temporal-community/temporal-dotnet-extensions/issues/7

## Context

DurableObject interface arguments are workflow data and become part of Temporal history. Client
transport concerns such as cancellation, deadlines, authorization, and tracing must not be added to
those arguments. Runtime proxies and generated clients also need one consistent contract.

The Temporal .NET SDK exposes cancellation, timeout, retry behavior, and gRPC text and binary
metadata through `RpcOptions` on both query and update-with-start calls.

## Decision

`DurableObjectCallOptions` is the immutable public transport contract. It snapshots caller-owned
metadata and maps each operation to a fresh SDK `RpcOptions` instance.

- Factory `Get` overloads bind options to a local client; every query or update through that client
  uses them.
- Named async query overloads accept the same options directly.
- `GetOrCreateAsync` applies the options to the start RPC and returns a client carrying them.
- Existing `CancellationToken` overloads remain source compatible and adapt to the new model.
- Cancellation stops the caller from waiting for an RPC. It does not undo an update already
  accepted by Temporal.
- Text and binary metadata are gRPC headers. Workflow payload headers remain the responsibility of
  Temporal interceptors and are not part of this API.

Generated clients will consume this same type rather than define a generator-specific context.

## Consequences

Transport values never enter workflow arguments or change handler wire names. Callers needing
different options create another inexpensive proxy. Connection-level metadata still applies, while
per-call metadata with the same key overrides it according to the Temporal SDK contract.
