using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TemporalCommunity.Extensions.Analyzers.Tests;

public sealed class TemporalWorkflowAnalyzerTests
{
    private const string WorkflowAttribute = """
        namespace Temporalio.Workflows
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WorkflowAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Property)]
            public sealed class WorkflowQueryAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class WorkflowUpdateAttribute : System.Attribute
            {
                public WorkflowUpdateAttribute() { }
                public WorkflowUpdateAttribute(string name) { }
            }
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class WorkflowUpdateValidatorAttribute : System.Attribute
            {
                public WorkflowUpdateValidatorAttribute(string updateMethod) { }
            }

            public static class Workflow
            {
                public static System.DateTime UtcNow => default;
                public static System.Threading.Tasks.Task DelayAsync(System.TimeSpan delay) =>
                    System.Threading.Tasks.Task.CompletedTask;
                public static System.Threading.Tasks.Task RunTaskAsync(System.Func<System.Threading.Tasks.Task> func) =>
                    func();
                public static System.Guid NewGuid() => default;
                public static System.Random Random => new System.Random();
                public static System.Threading.Tasks.Task ExecuteActivityAsync(
                    object call, ActivityOptions options) => System.Threading.Tasks.Task.CompletedTask;
                public static System.Threading.Tasks.Task ExecuteLocalActivityAsync(
                    object call, LocalActivityOptions options) => System.Threading.Tasks.Task.CompletedTask;
                public static System.Threading.Tasks.Task<System.Threading.Tasks.Task<T>> WhenAny<T>(
                    params System.Threading.Tasks.Task<T>[] tasks) => default!;
                public static System.Threading.Tasks.Task<System.Threading.Tasks.Task<T>> WhenAny<T>(
                    System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<T>> tasks) => default!;
            }

            public sealed class ActivityOptions
            {
                public System.TimeSpan? StartToCloseTimeout { get; set; }
                public System.TimeSpan? ScheduleToCloseTimeout { get; set; }
                public System.TimeSpan? HeartbeatTimeout { get; set; }
            }
            public sealed class LocalActivityOptions
            {
                public System.TimeSpan? StartToCloseTimeout { get; set; }
                public System.TimeSpan? ScheduleToCloseTimeout { get; set; }
            }
        }
        """;

    [Fact]
    public async Task ReportsConfigureAwaitFalseInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
                }
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ConfigureAwaitFalseId);
    }

    [Fact]
    public async Task ReportsTaskDelayInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Delay(10);
                }
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.TaskDelayId);
    }

    [Fact]
    public async Task ReportsSystemClockInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.DateTime Run() => System.DateTime.UtcNow;
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.SystemClockId);
    }

    [Fact]
    public async Task ReportsTaskRunBlockingAndRandomApisInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Run(() => System.Threading.Tasks.Task.CompletedTask);
                    System.Threading.Thread.Sleep(10);
                    using var source = new System.Threading.CancellationTokenSource(10);
                    _ = System.Guid.NewGuid();
                    _ = new System.Random();
                    _ = System.Random.Shared;
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.TaskRunId);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.BlockingWaitId));
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.NonDeterministicRandomId));
    }

    [Fact]
    public async Task ReportsCancellationTokenSourceCancelAsyncInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    using var source = new System.Threading.CancellationTokenSource();
                    await source.CancelAsync();
                    source.CancelAsync();
                }
            }
            """);

        Assert.Equal(2, diagnostics.Count(diagnostic =>
            diagnostic.Id == TemporalWorkflowAnalyzer.CancellationTokenSourceCancelAsyncId));
    }

    [Fact]
    public async Task ReportsInlineActivityOptionsWithoutRequiredTimeout()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await Temporalio.Workflows.Workflow.ExecuteActivityAsync(
                        null, new Temporalio.Workflows.ActivityOptions());
                    await Temporalio.Workflows.Workflow.ExecuteLocalActivityAsync(
                        null, new Temporalio.Workflows.LocalActivityOptions());
                }
            }
            """);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ActivityTimeoutId));
    }

    [Fact]
    public async Task AllowsInlineActivityTimeoutsAndNonInlineOptions()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await Temporalio.Workflows.Workflow.ExecuteActivityAsync(
                        null, new Temporalio.Workflows.ActivityOptions
                        {
                            StartToCloseTimeout = System.TimeSpan.FromSeconds(1)
                        });
                    await Temporalio.Workflows.Workflow.ExecuteLocalActivityAsync(
                        null, new Temporalio.Workflows.LocalActivityOptions
                        {
                            ScheduleToCloseTimeout = System.TimeSpan.FromSeconds(1)
                        });
                    var options = new Temporalio.Workflows.ActivityOptions();
                    await Temporalio.Workflows.Workflow.ExecuteActivityAsync(null, options);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ActivityTimeoutId);
    }

    [Fact]
    public async Task ReportsUnsafeGenericTaskWhenAnyShapes()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync(
                    System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<string>> tasks)
                {
                    await System.Threading.Tasks.Task.WhenAny(
                        System.Threading.Tasks.Task.FromResult("a"),
                        System.Threading.Tasks.Task.FromResult("b"),
                        System.Threading.Tasks.Task.FromResult("c"));
                    await System.Threading.Tasks.Task.WhenAny(tasks);
                }
            }
            """);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.UnsafeTaskWhenAnyId));
    }

    [Fact]
    public async Task AllowsSafeTaskWhenAnyShapesAndTaskWhenAll()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.WhenAny(
                        System.Threading.Tasks.Task.FromResult("a"),
                        System.Threading.Tasks.Task.FromResult("b"));
                    await System.Threading.Tasks.Task.WhenAny(
                        System.Threading.Tasks.Task.CompletedTask,
                        System.Threading.Tasks.Task.CompletedTask,
                        System.Threading.Tasks.Task.CompletedTask);
                    await System.Threading.Tasks.Task.WhenAll(
                        System.Threading.Tasks.Task.FromResult("a"),
                        System.Threading.Tasks.Task.FromResult("b"));
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.UnsafeTaskWhenAnyId);
    }

    [Fact]
    public async Task ReportsAsyncWorkflowQueryMethods()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                [Temporalio.Workflows.WorkflowQuery]
                public System.Threading.Tasks.Task<string> GetValueAsync() =>
                    System.Threading.Tasks.Task.FromResult("value");

                [Temporalio.Workflows.WorkflowQuery]
                public System.Threading.Tasks.Task GetEmptyAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;

                [Temporalio.Workflows.WorkflowQuery]
                public System.Threading.Tasks.ValueTask<string> GetValueTaskAsync() =>
                    new("value");

                [Temporalio.Workflows.WorkflowQuery]
                public System.Threading.Tasks.ValueTask GetEmptyValueTaskAsync() =>
                    System.Threading.Tasks.ValueTask.CompletedTask;
            }
            """);

        Assert.Equal(4, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowQueryAsyncId));
    }

    [Fact]
    public async Task AllowsSynchronousWorkflowQueriesAndIgnoresPropertiesAndNonWorkflows()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                [Temporalio.Workflows.WorkflowQuery]
                public string GetValue() => "value";

                [Temporalio.Workflows.WorkflowQuery]
                public string Value => "value";
            }

            public sealed class ActivityCode
            {
                [Temporalio.Workflows.WorkflowQuery]
                public System.Threading.Tasks.Task<string> GetValueAsync() =>
                    System.Threading.Tasks.Task.FromResult("value");
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowQueryAsyncId);
    }

    [Fact]
    public async Task ReportsInvalidWorkflowUpdateValidatorShapes()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                [Temporalio.Workflows.WorkflowUpdate("custom-name")]
                public System.Threading.Tasks.Task UpdateAsync(string value, int count) =>
                    System.Threading.Tasks.Task.CompletedTask;

                [Temporalio.Workflows.WorkflowUpdateValidator(nameof(UpdateAsync))]
                public string ValidateReturn(string value, int count) => "invalid";

                [Temporalio.Workflows.WorkflowUpdateValidator(nameof(UpdateAsync))]
                public void ValidateParameters(int count, string value) { }

                [Temporalio.Workflows.WorkflowUpdateValidator("MissingUpdate")]
                public void ValidateMissing() { }

                [Temporalio.Workflows.WorkflowUpdateValidator(nameof(UpdateAsync))]
                public void ValidateDuplicateOne(string value, int count) { }

                [Temporalio.Workflows.WorkflowUpdateValidator(nameof(UpdateAsync))]
                public void ValidateDuplicateTwo(string value, int count) { }
            }

            [Temporalio.Workflows.Workflow]
            public sealed class OtherWorkflow
            {
                [Temporalio.Workflows.WorkflowUpdateValidator("UpdateAsync")]
                public void ValidateOther() { }
            }
            """);

        Assert.Equal(1, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowUpdateValidatorReturnId));
        Assert.Equal(1, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowUpdateValidatorParametersId));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowUpdateValidatorTargetId));
        Assert.Equal(4, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.WorkflowUpdateValidatorDuplicateId));
    }

    [Fact]
    public async Task AllowsMatchingWorkflowUpdateValidator()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                [Temporalio.Workflows.WorkflowUpdate]
                public System.Threading.Tasks.Task UpdateAsync(string value, int count) =>
                    System.Threading.Tasks.Task.CompletedTask;

                [Temporalio.Workflows.WorkflowUpdateValidator(nameof(UpdateAsync))]
                public void ValidateUpdate(string value, int count) { }
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is
            TemporalWorkflowAnalyzer.WorkflowUpdateValidatorReturnId or
            TemporalWorkflowAnalyzer.WorkflowUpdateValidatorParametersId or
            TemporalWorkflowAnalyzer.WorkflowUpdateValidatorTargetId or
            TemporalWorkflowAnalyzer.WorkflowUpdateValidatorDuplicateId);
    }

    [Fact]
    public async Task DoesNotReportCancellationTokenSourceCancelAsyncOutsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed class ActivityCode
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    using var source = new System.Threading.CancellationTokenSource();
                    await source.CancelAsync();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsCryptographicRandomApisInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    using var generator = System.Security.Cryptography.RandomNumberGenerator.Create();
                    generator.GetBytes(new byte[4]);
                    generator.GetNonZeroBytes(new byte[4]);
                    System.Security.Cryptography.RandomNumberGenerator.Fill(new byte[4]);
                    _ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10);
                }
            }
            """);

        Assert.Equal(5, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.NonDeterministicRandomId));
    }

    [Fact]
    public async Task DoesNotReportCryptographicRandomApisOutsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed class ActivityCode
            {
                public void Run()
                {
                    using var generator = System.Security.Cryptography.RandomNumberGenerator.Create();
                    generator.GetBytes(new byte[4]);
                    _ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsUnorderedCollectionEnumerationInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    var dictionary = new System.Collections.Generic.Dictionary<string, int>();
                    var hashSet = new System.Collections.Generic.HashSet<int>();
                    var concurrent = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();

                    foreach (var item in dictionary) { _ = item; }
                    foreach (var item in dictionary.Keys) { _ = item; }
                    foreach (var item in dictionary.Values) { _ = item; }
                    foreach (var item in hashSet) { _ = item; }
                    foreach (var item in concurrent) { _ = item; }
                    foreach (var item in concurrent.Keys) { _ = item; }
                    foreach (var item in concurrent.Values) { _ = item; }
                    foreach (var (key, value) in dictionary) { _ = key; _ = value; }
                }
            }
            """);

        Assert.Equal(8, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.UnorderedCollectionId));
    }

    [Fact]
    public async Task AllowsOrderedCollectionEnumerationAndNonWorkflowCode()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    var array = new[] { 1, 2, 3 };
                    var list = new System.Collections.Generic.List<int>();
                    var sortedDictionary = new System.Collections.Generic.SortedDictionary<string, int>();
                    var sortedSet = new System.Collections.Generic.SortedSet<int>();

                    foreach (var item in array) { _ = item; }
                    foreach (var item in list) { _ = item; }
                    foreach (var item in sortedDictionary) { _ = item; }
                    foreach (var item in sortedSet) { _ = item; }
                }
            }

            public sealed class ActivityCode
            {
                public void Run()
                {
                    var dictionary = new System.Collections.Generic.Dictionary<string, int>();
                    foreach (var item in dictionary) { _ = item; }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsSynchronizationAndConsoleIoInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    lock (this)
                    {
                        System.Threading.Monitor.Enter(this);
                        System.Threading.Monitor.TryEnter(this);
                        System.Threading.Monitor.Exit(this);
                        System.Threading.Monitor.Wait(this);
                        System.Threading.Monitor.Pulse(this);
                        System.Threading.Monitor.PulseAll(this);
                    }

                    System.Console.Write("hello");
                    System.Console.WriteLine("world");
                    _ = System.Console.In;
                    _ = System.Console.Out;
                    _ = System.Console.Error;
                }
            }
            """);

        Assert.Equal(7, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.SynchronizationId));
        Assert.Equal(5, diagnostics.Count(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ConsoleIoId));

        var consoleDiagnostic = Assert.Single(
            diagnostics.Where(diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ConsoleIoId &&
                diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("Console.WriteLine", StringComparison.Ordinal)));
        Assert.Equal(DiagnosticSeverity.Warning, consoleDiagnostic.Severity);
        Assert.Equal("Temporal.Observability", consoleDiagnostic.Descriptor.Category);
    }

    [Fact]
    public async Task DoesNotReportSynchronizationOrConsoleIoOutsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed class ActivityCode
            {
                public void Run()
                {
                    lock (this)
                    {
                        System.Threading.Monitor.TryEnter(this);
                        System.Threading.Monitor.Exit(this);
                    }

                    System.Console.WriteLine("hello");
                    _ = System.Console.Error;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsTemporalAlternativesAndNonWorkflowCode()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    _ = Temporalio.Workflows.Workflow.UtcNow;
                    await Temporalio.Workflows.Workflow.DelayAsync(System.TimeSpan.FromSeconds(1));
                    await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(true);
                }
            }

            public sealed class ActivityCode
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    _ = System.DateTime.UtcNow;
                    await System.Threading.Tasks.Task.Delay(1).ConfigureAwait(false);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(WorkflowAttribute),
            CSharpSyntaxTree.ParseText(source),
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compilerErrors);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TemporalWorkflowAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Contains(diagnostic.Severity, new[] { DiagnosticSeverity.Error, DiagnosticSeverity.Warning });
            Assert.True(diagnostic.Location.IsInSource);
        });
        return diagnostics;
    }
}
