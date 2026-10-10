using System.Reflection;
using System.Reflection.Emit;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Temporalio.Converters;
using Temporalio.Extensions.Hosting;
using Temporalio.Worker;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

/// <summary>
/// Tests for AddDurableObjectWorkflows assembly scan behavior. All assertions are structural
/// (no live Temporal server required).
/// </summary>
public sealed class WorkerExtensionTests
{
    // AddDurableObjectWorkflows scanning the library assembly registers ReminderDispatcher.
    [Fact]
    public void AddDurableObjectWorkflows_RegistersReminderDispatcherFromLibraryAssembly()
    {
        var options = new TemporalWorkerOptions("test-queue");
        // Scan the library assembly — its only concrete DurableObjectBase subclass is ReminderDispatcher.
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.Contains(registered, t => t.Name == "ReminderDispatcher");
    }

    // Abstract DurableObjectBase is never registered as a workflow.
    [Fact]
    public void AddDurableObjectWorkflows_ExcludesAbstractBaseClass()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.DoesNotContain(typeof(DurableObjectBase), registered);
    }

    // Only DurableObjectBase subclasses appear in registered workflows from the library assembly.
    [Fact]
    public void AddDurableObjectWorkflows_RegistersOnlyDurableObjectBaseSubclasses()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.All(registered, t =>
            Assert.True(
                t.Name == "ReminderDispatcher" || typeof(DurableObjectBase).IsAssignableFrom(t),
                $"Unexpected type registered: {t.FullName}"));
    }

    [Fact]
    public void AddDurableObjectWorkflows_RegistersSignalMethods()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(SignalBearingWorkflow).Assembly);
        Assert.Contains(typeof(SignalBearingWorkflow), GetRegisteredWorkflowTypes(options));
    }

    [Fact]
    public void AddDurableObjectWorkflows_RequiresSignalAuthWithUpdateAuth()
    {
        var options = new TemporalWorkerOptions("test-queue");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.AddDurableObjectWorkflows(typeof(SignalBearingWorkflow).Assembly,
                new DurableObjectWorkerOptions { Authorize = _ => true }));
        Assert.Contains("AuthorizeSignal", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDurableObjectWorkflows_AcceptsBothAuthCallbacks()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(SignalBearingWorkflow).Assembly,
            new DurableObjectWorkerOptions { Authorize = _ => true, AuthorizeSignal = _ => true });
        Assert.Contains(typeof(SignalBearingWorkflow), GetRegisteredWorkflowTypes(options));
    }

    [Fact]
    public void AddDurableObjectWorkflows_ChecksPreviouslyRegisteredSignals()
    {
        var options = new TemporalWorkerOptions("test-queue").AddWorkflow<SignalBearingWorkflow>();
        Assert.Throws<InvalidOperationException>(() =>
            options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly,
                new DurableObjectWorkerOptions { Authorize = _ => true }));
    }

    // ReminderDispatcher is always registered (internal framework workflow).
    [Fact]
    public void AddDurableObjectWorkflows_AlwaysRegistersReminderDispatcher()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.Contains(registered, t => t.Name == "ReminderDispatcher");
    }

    // DurableObjectWorkerOptions defaults: Serialize == true, Authorize == null.
    [Fact]
    public void DurableObjectWorkerOptions_HasCorrectDefaults()
    {
        var opts = new DurableObjectWorkerOptions();
        Assert.True(opts.Serialize);
        Assert.Null(opts.Authorize);
        Assert.Null(opts.AuthorizeSignal);
    }

    // --- helpers ---

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Registration_RejectsDynamicSignalsRegardlessOfAuthorization(bool hosted, bool preregistered)
    {
        foreach (var named in new[] { false, true })
        {
            var type = CreateSignalWorkflow(dynamic: true, named);
            var definition = WorkflowDefinition.Create(type);
            Assert.NotNull(definition.DynamicSignal);
            Assert.Equal(named ? 1 : 0, definition.Signals.Count);
            foreach (var authorization in new[]
            {
                new DurableObjectWorkerOptions(),
                new DurableObjectWorkerOptions { Authorize = _ => true },
                new DurableObjectWorkerOptions { AuthorizeSignal = _ => true },
                new DurableObjectWorkerOptions { Authorize = _ => true, AuthorizeSignal = _ => true },
            })
            {
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    ConfigureRegistration(type, hosted, preregistered, authorization));
                Assert.Contains("DynamicSignal", ex.Message, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Registration_NamedSignalsRequireSignalAuthOnlyWhenUpdateAuthConfigured(bool hosted, bool preregistered)
    {
        var type = CreateSignalWorkflow(dynamic: false, named: true);
        ConfigureRegistration(type, hosted, preregistered, new DurableObjectWorkerOptions());
        ConfigureRegistration(type, hosted, preregistered,
            new DurableObjectWorkerOptions { Authorize = _ => true, AuthorizeSignal = _ => true });
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigureRegistration(type, hosted, preregistered,
                new DurableObjectWorkerOptions { Authorize = _ => true }));
        Assert.Contains("AuthorizeSignal", ex.Message, StringComparison.Ordinal);
    }

    private static void ConfigureRegistration(
        Type type, bool hosted, bool preregistered, DurableObjectWorkerOptions authorization)
    {
        var scan = preregistered ? typeof(DurableObjectBase).Assembly : type.Assembly;
        if (!hosted)
        {
            var options = new TemporalWorkerOptions("test-queue");
            if (preregistered) options.AddWorkflow(type);
            options.AddDurableObjectWorkflows(scan, authorization);
            return;
        }

        var services = new ServiceCollection();
        var builder = A.Fake<ITemporalWorkerServiceOptionsBuilder>();
        A.CallTo(() => builder.Services).Returns(services);
        A.CallTo(() => builder.TaskQueue).Returns("test-queue");
        if (preregistered) builder.AddWorkflow(type);
        builder.AddDurableObjectWorkflows(scan, authorization);
        var optionsName = builder.ConfigureOptions().Name;
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptionsMonitor<TemporalWorkerServiceOptions>>()
            .Get(optionsName);
    }

    // Isolated assemblies avoid adding unsupported definitions to existing assembly-scan tests.
    private static Type CreateSignalWorkflow(bool dynamic, bool named)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"SignalRegistration{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Tests").DefineType(
            "SignalWorkflow", TypeAttributes.Public | TypeAttributes.Sealed, typeof(DurableObjectBase));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(WorkflowAttribute).GetConstructor(Type.EmptyTypes)!, []));
        AddTaskMethod("RunAsync", [], new CustomAttributeBuilder(
            typeof(WorkflowRunAttribute).GetConstructor(Type.EmptyTypes)!, []));
        if (named)
            AddTaskMethod("NamedAsync", [], new CustomAttributeBuilder(
                typeof(WorkflowSignalAttribute).GetConstructor([typeof(string)])!, [null!]));
        if (dynamic)
            AddTaskMethod("DynamicAsync", [typeof(string), typeof(IRawValue[])], new CustomAttributeBuilder(
                typeof(WorkflowSignalAttribute).GetConstructor([typeof(string)])!, [null!],
                [typeof(WorkflowSignalAttribute).GetProperty("Dynamic")!], [true]));
        return type.CreateType()!;

        void AddTaskMethod(string name, Type[] parameters, CustomAttributeBuilder attribute)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public, typeof(Task), parameters);
            method.SetCustomAttribute(attribute);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
            il.Emit(OpCodes.Ret);
        }
    }

    private static IEnumerable<Type> GetRegisteredWorkflowTypes(TemporalWorkerOptions options) =>
        options.Workflows.Select(wd => wd.Type);
}

// --- test workflow types at namespace level (not nested) to avoid CA1034 ---

[Workflow]
internal sealed class SignalBearingWorkflow : DurableObjectBase
{
    [WorkflowRun]
#pragma warning disable CA1822 // Mark members as static — [WorkflowRun] must be instance
    public Task RunAsync() => DurableObjectRunAsync();
#pragma warning restore CA1822

    [WorkflowSignal]
    public Task SomethingSignaledAsync() => Task.CompletedTask;
}
