using System.Reflection;
using Temporalio.Extensions.Hosting;
using Temporalio.Worker;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects.Polyfills;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Extension methods for registering DurableObject workflows on a Temporal worker.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hosted workers (<see cref="ITemporalWorkerServiceOptionsBuilder"/>):</b>
/// <code>
/// services.AddHostedTemporalWorker("my-task-queue")
///         .AddDurableObjectWorkflows(typeof(Counter).Assembly);
/// </code>
/// </para>
/// <para>
/// <b>Non-hosted workers (<see cref="TemporalWorkerOptions"/>):</b>
/// <code>
/// var workerOptions = new TemporalWorkerOptions("my-task-queue");
/// workerOptions.AddDurableObjectWorkflows(typeof(Counter).Assembly);
/// </code>
/// </para>
/// <para>
/// <b>What <c>AddDurableObjectWorkflows</c> does:</b>
/// <list type="number">
///   <item><description>
///     Scans the provided assembly for concrete (non-abstract) types that inherit from
///     <see cref="DurableObjectBase"/> and carry the <c>[Workflow]</c> attribute.
///   </description></item>
///   <item><description>
///     For each found type: calls <c>WorkflowDefinition.Create(type)</c> for SDK-level validation
///     (catches missing <c>[WorkflowRun]</c>, bad signatures, etc. at startup rather than at
///     first execution).
///   </description></item>
///   <item><description>
///     Rejects any scanned type that declares a <c>[WorkflowSignal]</c> method — signals are
///     banned in v1 (see ADR <c>005-signals-banned.md</c>).
///   </description></item>
///   <item><description>
///     Registers each valid type as a workflow, plus <see cref="ReminderDispatcher"/> (always —
///     it is internal infrastructure required for canonical-object reminders).
///   </description></item>
///   <item><description>
///     Auto-installs <see cref="DurableObjectWorkerInterceptor"/> with the provided options
///     (or defaults if <see langword="null"/>). Without the interceptor, serialization,
///     exception wrapping, and drain-window gating are all inactive.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public static class DurableObjectWorkerExtensions
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for DurableObject workflow types, registers them on
    /// this hosted worker, and auto-installs <see cref="DurableObjectWorkerInterceptor"/> with
    /// default options (<c>serialize: true</c>, no authorization predicate).
    /// </summary>
    /// <param name="builder">The hosted worker builder to configure.</param>
    /// <param name="assembly">The assembly to scan for DurableObject types.</param>
    /// <param name="options">
    /// Optional interceptor options. When <see langword="null"/>, defaults are used
    /// (<c>Serialize = true</c>, <c>Authorize = null</c>).
    /// </param>
    /// <returns>The same builder instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="assembly"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a scanned type carries a <c>[WorkflowSignal]</c> method (banned in v1) or
    /// when <c>WorkflowDefinition.Create(type)</c> fails validation.
    /// </exception>
    public static ITemporalWorkerServiceOptionsBuilder AddDurableObjectWorkflows(
        this ITemporalWorkerServiceOptionsBuilder builder,
        Assembly assembly,
        DurableObjectWorkerOptions? options = null)
    {
        Throw.IfNull(builder, nameof(builder));
        Throw.IfNull(assembly, nameof(assembly));

        return builder.ConfigureOptions(workerOptions =>
            ConfigureDurableObjectWorkflows(workerOptions, assembly, options));
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for DurableObject workflow types, registers them on
    /// this non-hosted worker, and auto-installs <see cref="DurableObjectWorkerInterceptor"/>
    /// with default options (<c>serialize: true</c>, no authorization predicate).
    /// </summary>
    /// <param name="workerOptions">The worker options to configure.</param>
    /// <param name="assembly">The assembly to scan for DurableObject types.</param>
    /// <param name="options">
    /// Optional interceptor options. When <see langword="null"/>, defaults are used.
    /// </param>
    /// <returns>The same options instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="workerOptions"/> or <paramref name="assembly"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a scanned type carries a <c>[WorkflowSignal]</c> method (banned in v1) or
    /// when <c>WorkflowDefinition.Create(type)</c> fails validation.
    /// </exception>
    public static TemporalWorkerOptions AddDurableObjectWorkflows(
        this TemporalWorkerOptions workerOptions,
        Assembly assembly,
        DurableObjectWorkerOptions? options = null)
    {
        Throw.IfNull(workerOptions, nameof(workerOptions));
        Throw.IfNull(assembly, nameof(assembly));

        ConfigureDurableObjectWorkflows(workerOptions, assembly, options);
        return workerOptions;
    }

    // ---------------------------------------------------------------------------
    // Shared implementation
    // ---------------------------------------------------------------------------

    private static void ConfigureDurableObjectWorkflows(
        TemporalWorkerOptions workerOptions,
        Assembly assembly,
        DurableObjectWorkerOptions? options)
    {
        options ??= new DurableObjectWorkerOptions();

        // Scan for concrete DurableObjectBase subclasses with [Workflow].
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract ||
                !typeof(DurableObjectBase).IsAssignableFrom(type) ||
                type.GetCustomAttribute<WorkflowAttribute>() is null)
            {
                continue;
            }

            // v1 policy: [WorkflowSignal] is banned on DurableObjects.
            // Validate before WorkflowDefinition.Create so the error is clear.
            RejectSignalMethods(type);

            // SDK-level validation: catches missing [WorkflowRun], bad signatures, etc.
            // WorkflowDefinition.Create throws ArgumentException on invalid types.
            workerOptions.AddWorkflow(type);
        }

        // Always register the framework's reminder dispatcher — it is infrastructure
        // required for canonical-object reminders regardless of whether user types use them.
        workerOptions.AddWorkflow<ReminderDispatcher>();

        // Auto-install DurableObjectWorkerInterceptor. Without this, the serialization gate,
        // drain-window gate, and exception safety net are all inert.
        // Preserve any interceptors already registered (e.g., OTel TracingInterceptor).
        var interceptor = new DurableObjectWorkerInterceptor(options.Serialize, options.Authorize);
        workerOptions.Interceptors = workerOptions.Interceptors is { } existing
            ? [.. existing, interceptor]
            : [interceptor];
    }

    private static void RejectSignalMethods(Type type)
    {
        foreach (var method in type.GetMethods())
        {
            if (method.IsDefined(typeof(WorkflowSignalAttribute), inherit: false))
            {
                throw new InvalidOperationException(
                    $"DurableObject type '{type.FullName}' declares '{method.Name}' with " +
                    $"[WorkflowSignal]. Signals are banned in v1 — all handlers must use " +
                    $"[WorkflowUpdate] or [WorkflowQuery]. See docs/adr/005-signals-banned.md.");
            }
        }
    }
}
