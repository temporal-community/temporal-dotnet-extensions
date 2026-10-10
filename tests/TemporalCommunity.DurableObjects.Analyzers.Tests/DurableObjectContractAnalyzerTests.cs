using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TemporalCommunity.DurableObjects.Analyzers.Tests;

public sealed class DurableObjectContractAnalyzerTests
{
    private const string Framework = """
        namespace Temporalio.Workflows
        {
            public sealed class WorkflowAttribute : System.Attribute { }
            public sealed class WorkflowRunAttribute : System.Attribute { }
            public sealed class WorkflowInitAttribute : System.Attribute { }
            public sealed class WorkflowUpdateAttribute : System.Attribute { }
            public sealed class WorkflowQueryAttribute : System.Attribute { }
            public sealed class WorkflowSignalAttribute : System.Attribute { public bool Dynamic { get; set; } }
        }

        namespace TemporalCommunity.DurableObjects
        {
            public interface IDurableObject { }
            public abstract class DurableObjectBase
            {
                [Temporalio.Workflows.WorkflowUpdate]
                public virtual System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            public abstract class DurableObjectBase<TState> : DurableObjectBase where TState : notnull { }
            public sealed class DurableObjectSnapshot<TState> where TState : notnull { }
        }
        """;

    [Fact]
    public async Task ReportsInvalidContractShapeButAllowsSignal()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public interface IBadObject : IDurableObject
            {
                int MissingQuery();
                [WorkflowSignal] System.Threading.Tasks.Task WakeAsync();
            }
            """);

        Assert.Equal(DurableObjectContractAnalyzer.InvalidContractMethodId, Assert.Single(diagnostics).Id);
        Assert.DoesNotContain(new DurableObjectContractAnalyzer().SupportedDiagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.SignalNotSupportedId);
    }

    [Theory]
    [InlineData("[WorkflowSignal] int Wake();")]
    [InlineData("[WorkflowSignal(Dynamic = true)] System.Threading.Tasks.Task WakeAsync();")]
    [InlineData("[WorkflowSignal] void Wake();")]
    [InlineData("[WorkflowSignal] System.Threading.Tasks.Task<int> WakeAsync();")]
    [InlineData("[WorkflowSignal] System.Threading.Tasks.ValueTask WakeAsync();")]
    [InlineData("[WorkflowSignal, WorkflowUpdate] System.Threading.Tasks.Task WakeAsync();")]
    [InlineData("[WorkflowSignal, WorkflowQuery] int Wake();")]
    [InlineData("[WorkflowUpdate, WorkflowQuery] System.Threading.Tasks.Task WakeAsync();")]
    [InlineData("[WorkflowSignal, WorkflowUpdate, WorkflowQuery] System.Threading.Tasks.Task WakeAsync();")]
    public async Task RejectsInvalidSignalReturnsAndMultipleHandlerAttributes(string member)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using Temporalio.Workflows;
            public interface IBad : TemporalCommunity.DurableObjects.IDurableObject
            {
                {{member}}
            }
            """);

        Assert.Equal(DurableObjectContractAnalyzer.InvalidContractMethodId, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task AllowsSignalsOnConcreteAndAbstractObjects()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;
            public abstract class Parent : DurableObjectBase
            {
                [WorkflowSignal] public System.Threading.Tasks.Task WakeAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            public sealed class Child : Parent
            {
                [WorkflowRun] public System.Threading.Tasks.Task RunAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
                [WorkflowSignal] public System.Threading.Tasks.Task NotifyAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("[WorkflowSignal] public int Bad() => 0;")]
    [InlineData("[WorkflowSignal(Dynamic = true)] public System.Threading.Tasks.Task BadAsync() => System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("[WorkflowSignal] public System.Threading.Tasks.Task<int> BadAsync() => System.Threading.Tasks.Task.FromResult(0);")]
    [InlineData("[WorkflowSignal, WorkflowUpdate] public System.Threading.Tasks.Task BadAsync() => System.Threading.Tasks.Task.CompletedTask;")]
    public async Task InvalidConcreteSignalReportsDO0001(string member)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using Temporalio.Workflows;
            public sealed class BadObject : TemporalCommunity.DurableObjects.DurableObjectBase
            {
                [WorkflowRun] public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
                {{member}}
            }
            """);
        Assert.Equal(DurableObjectContractAnalyzer.InvalidContractMethodId, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsMissingRunOnConcreteObject()
    {
        var diagnostics = await AnalyzeAsync("""
            using TemporalCommunity.DurableObjects;
            public sealed class BadObject : DurableObjectBase { }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.MissingWorkflowRunId);
    }

    [Fact]
    public async Task RejectsDynamicSignalDeclaredOnAbstractAncestor()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            public abstract class Parent : TemporalCommunity.DurableObjects.DurableObjectBase
            {
                [WorkflowSignal(Dynamic = true)]
                public System.Threading.Tasks.Task CatchAllAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            public sealed class Child : Parent
            {
                [WorkflowRun] public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """);
        Assert.Equal(DurableObjectContractAnalyzer.InvalidContractMethodId, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsInvalidTypedStateSignature()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class BadObject : DurableObjectBase<int>
            {
                [WorkflowInit] public BadObject() { }
                [WorkflowRun] public System.Threading.Tasks.Task RunAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.InvalidTypedStateSignatureId);
    }

    [Fact]
    public async Task ReportsDeactivateAsyncOverrideMissingWorkflowUpdate()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class BadObject : DurableObjectBase
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task AllowsDeactivateAsyncOverrideThatRetainsWorkflowUpdate()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class GoodObject : DurableObjectBase
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                [WorkflowUpdate]
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task DoesNotReportWhenDeactivateAsyncIsNotOverridden()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class PlainObject : DurableObjectBase
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task DoesNotReportForNonOverrideMethodNamedDeactivateAsync()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class ShadowingObject : DurableObjectBase
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                public new System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task ReportsDeactivateAsyncOverrideAcrossMultiLevelChain()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public abstract class MidObject : DurableObjectBase
            {
                [WorkflowUpdate]
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class LeafObject : MidObject
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task ReportsDeactivateAsyncOverrideDeclaredOnAbstractAncestorNotRedeclaredByLeaf()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public abstract class MidObject : DurableObjectBase
            {
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class LeafObject : MidObject
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task AllowsAbstractAncestorOverrideThatRetainsWorkflowUpdateWhenLeafDoesNotRedeclare()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public abstract class MidObject : DurableObjectBase
            {
                [WorkflowUpdate]
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }

            public sealed class LeafObject : MidObject
            {
                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);
    }

    [Fact]
    public async Task AllowsValidContractAndTypedStateObject()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public interface ICounter : IDurableObject
            {
                [WorkflowUpdate] System.Threading.Tasks.Task IncrementAsync();
                [WorkflowQuery] int GetCount();
            }

            public sealed class Counter : DurableObjectBase<int>, ICounter
            {
                [WorkflowInit]
                public Counter(DurableObjectSnapshot<int>? snapshot = null) { }

                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync(
                    DurableObjectSnapshot<int>? snapshot = null) =>
                    System.Threading.Tasks.Task.CompletedTask;

                [WorkflowUpdate]
                public System.Threading.Tasks.Task IncrementAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;

                [WorkflowQuery]
                public int GetCount() => 0;
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(Framework),
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
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DurableObjectContractAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.True(diagnostic.Location.IsInSource);
        });
        return diagnostics;
    }
}
