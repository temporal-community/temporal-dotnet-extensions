# Workflow update-validator shape

Status: Complete

Tracking: Plan 009, Phase 2

## Evidence

The local Temporal .NET SDK validates update validators in
`Temporalio.Workflows.WorkflowUpdateDefinition`:

- The update method must return `Task` or a subtype assignable to `Task`.
- The validator must be public and non-static.
- The validator must return `void`.
- The validator parameter types must match the update method parameter types. The SDK compares
  reflection `ParameterType` values only; it does not separately compare `ref`, `in`, or `out`
  metadata when those values expose the same by-ref type.
- `WorkflowUpdateValidatorAttribute.UpdateMethod` identifies the update by its CLR method name,
  not its custom workflow update name.
- Workflow discovery reports an error when more than one validator targets the same CLR update
  method name ([`WorkflowDefinition.cs:239-242`](../../../../workspace/temporal-sdk-dotnet/src/Temporalio/Workflows/WorkflowDefinition.cs#L239-L242)).
  This uniqueness check is scoped to validators discovered on one workflow type.
- Workflow discovery reports an orphan validator when no update method with that CLR name exists.
- Generic validator methods are rejected during workflow definition discovery.

The SDK documentation and the local Temporal developer skill also state that validators are
read-only, must not issue commands, and reject an update by throwing an exception.

## Analyzer scope

Analyze only methods inside `[Workflow]` types carrying
`[WorkflowUpdateValidator(nameof(...))]`. Resolve the attribute constructor string and compare it
with methods in the same workflow type.

All diagnostics belong to the `Temporal.WorkflowShape` category. Initial diagnostics should be
separate and precise:

- `TEMP016`: validator return type is not `void`.
- `TEMP017`: validator parameter count or `ParameterType` values do not match the associated update
  method. Do not add a stricter Roslyn `RefKind` comparison unless the SDK contract changes.
- `TEMP018`: the attribute names no update method carrying `[WorkflowUpdate]`.
- `TEMP019`: more than one validator targets the same update method name.

The first implementation should also suppress analysis when the attribute argument is not a
compile-time string or the target method cannot be resolved unambiguously. It should not infer
relationships through helpers, inheritance, or generated code beyond symbols visible in the same
compilation.

Public/static/generic-validator checks are exact SDK contracts, but should be added as a follow-up
diagnostic only after the four initial checks have stable test coverage. This keeps the
initial rule set small and avoids bundling unrelated declaration-shape errors.

## Code-fix decision

Do not offer automatic fixes initially. Changing a validator's return type or parameter list can
alter application behavior or discard validation inputs. Removing an orphan validator attribute is
also destructive. Removing one of two duplicate validator attributes is likewise unsafe because
both validators may contain intentional validation behavior and selecting the correct one requires
application intent. The code-fix project should explicitly leave TEMP016–TEMP019 non-fixable and
test that no actions are offered.

## Tests

Analyzer tests should cover:

- valid `void` validator with matching parameters;
- non-`void` validator;
- parameter count/type mismatch;
- ref/in/out-only differences remain an explicit non-goal because the SDK compares
  reflection `ParameterType` values without a separate ref-kind check;
- missing target update;
- target method without `[WorkflowUpdate]`;
- custom update names still resolving by CLR method name;
- duplicate validators targeting one update;
- validators and updates in separate workflow types remaining isolated;
- non-workflow code and unrelated attributes remaining silent;
- unresolved/non-constant attribute arguments remaining silent.

Code-fix tests should verify TEMP016–TEMP019 produce no code actions.

## Documentation

When implemented:

- add TEMP016–TEMP019 to `docs/ANALYZERS.md` with their exact boundaries and no-fix status;
- add a concise validator example showing a synchronous `void` validator that throws to reject an
  update;
- update the rule catalog status and confidence entries;
- update README or samples only if an existing entry point or sample demonstrates update
  validators; otherwise do not add low-value duplicated material;
- keep implementation boundaries, SDK evidence, and deferred public/static/generic checks in
  `plans/`.

## Completion criteria

- TEMP016–TEMP019 are implemented in the general analyzer package.
- The code-fix package offers no actions for these diagnostics and has regression tests proving it.
- Analyzer tests cover valid and invalid relationship cases without relying on runtime startup.
- `docs/ANALYZERS.md` and the rule catalog describe the shipped rules and their limits.
- The solution builds cleanly and all relevant tests pass.
