using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Root interface that every DurableObject must implement.
/// All user-defined DurableObject interfaces should extend <see cref="IDurableObject"/> so that
/// lifecycle control (deactivation) is reachable through a typed proxy without casting.
/// </summary>
/// <remarks>
/// The <see cref="DeactivateAsync"/> method is declared <c>[WorkflowUpdate]</c> here to serve
/// as a compile-time contract. The Temporal SDK does not inherit interface attributes onto
/// concrete classes (it scans with <c>IsDefined(attr, false)</c>), so
/// <see cref="DurableObjectBase"/> also declares <c>[WorkflowUpdate]</c> on its own
/// <see cref="DurableObjectBase.DeactivateAsync"/> implementation.
/// </remarks>
[Workflow]
public interface IDurableObject
{
    /// <summary>
    /// Requests graceful deactivation of this object.
    /// The handler sets the deactivating flag and returns immediately; the run loop then drains
    /// any in-flight update handlers before calling <c>OnDeactivateAsync</c> and completing.
    /// </summary>
    /// <remarks>
    /// Implemented as a <c>[WorkflowUpdate]</c> (not a signal) so that the caller receives
    /// confirmation that the deactivation request was accepted, and so that the update goes
    /// through any registered authorization predicate on <c>DurableObjectWorkerInterceptor</c>.
    /// Subclasses that override this method must redeclare <c>[WorkflowUpdate]</c> on the
    /// override — the SDK does not inherit the attribute from the base class or interface.
    /// Prefer overriding <see cref="DurableObjectBase.OnDeactivateAsync"/> instead.
    /// </remarks>
    [WorkflowUpdate]
    Task DeactivateAsync();
}
