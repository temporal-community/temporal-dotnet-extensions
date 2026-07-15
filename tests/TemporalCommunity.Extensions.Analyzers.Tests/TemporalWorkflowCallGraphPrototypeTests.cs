using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace TemporalCommunity.Extensions.Analyzers.Tests;

public sealed class TemporalWorkflowCallGraphPrototypeTests
{
    private const string WorkflowAttribute = """
        namespace Temporalio.Workflows
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WorkflowAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void PropagatesExistingDeterminismFindingThroughSameAssemblyHelper()
    {
        var findings = Analyze("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.Threading.Tasks.Task RunAsync() => Helpers.ReadClockAsync();
            }

            internal static class Helpers
            {
                public static System.Threading.Tasks.Task ReadClockAsync() =>
                    System.Threading.Tasks.Task.FromResult(System.DateTime.UtcNow);
            }
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("MyWorkflow.RunAsync()", finding.MethodName);
        Assert.Contains("Helpers.ReadClockAsync", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotReportSafeHelpers()
    {
        var findings = Analyze("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.Threading.Tasks.Task RunAsync() => Helpers.ReadConstantAsync();
            }

            internal static class Helpers
            {
                public static System.Threading.Tasks.Task ReadConstantAsync() =>
                    System.Threading.Tasks.Task.FromResult(System.DateTime.UnixEpoch);
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void TerminatesOnRecursiveCallCycles()
    {
        var findings = Analyze("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.Threading.Tasks.Task RunAsync() => Helpers.FirstAsync();
            }

            internal static class Helpers
            {
                public static System.Threading.Tasks.Task FirstAsync() => SecondAsync();

                public static System.Threading.Tasks.Task SecondAsync() => FirstAsync();
            }
            """);

        Assert.Empty(findings);
    }

    private static ImmutableArray<PrototypeFinding> Analyze(string source)
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(WorkflowAttribute),
            CSharpSyntaxTree.ParseText(source),
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "CallGraphPrototype",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return TemporalWorkflowCallGraphPrototype.Analyze(compilation);
    }

    private sealed record PrototypeFinding(string MethodName, string Reason);

    private static class TemporalWorkflowCallGraphPrototype
    {
        public static ImmutableArray<PrototypeFinding> Analyze(Compilation compilation)
        {
            var methods = new Dictionary<IMethodSymbol, MethodDeclarationSyntax>(SymbolEqualityComparer.Default);
            var models = new Dictionary<SyntaxTree, SemanticModel>();

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                models[tree] = model;
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is IMethodSymbol method)
                    {
                        methods[method.OriginalDefinition] = declaration;
                    }
                }
            }

            var directReasons = new Dictionary<IMethodSymbol, string>(SymbolEqualityComparer.Default);
            var calls = new Dictionary<IMethodSymbol, List<(IMethodSymbol Target, string Name)>>(SymbolEqualityComparer.Default);

            foreach (var (method, declaration) in methods)
            {
                var model = models[declaration.SyntaxTree];
                foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol target)
                    {
                        continue;
                    }

                    if (IsNondeterministicInvocation(target))
                    {
                        directReasons.TryAdd(method, $"calls {target.ContainingType.ToDisplayString()}.{target.Name}");
                    }

                    var targetDefinition = target.OriginalDefinition;
                    if (methods.ContainsKey(targetDefinition))
                    {
                        if (!calls.TryGetValue(method, out var targets))
                        {
                            targets = new List<(IMethodSymbol Target, string Name)>();
                            calls[method] = targets;
                        }

                        targets.Add((targetDefinition, targetDefinition.ToDisplayString()));
                    }
                }

                foreach (var memberAccess in declaration.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property &&
                        property.ContainingType.ToDisplayString() == "System.DateTime" &&
                        property.Name is "Now" or "UtcNow")
                    {
                        directReasons.TryAdd(method, $"accesses System.DateTime.{property.Name}");
                    }
                }
            }

            var reasons = new Dictionary<IMethodSymbol, string>(directReasons, SymbolEqualityComparer.Default);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (caller, targets) in calls)
                {
                    if (reasons.ContainsKey(caller))
                    {
                        continue;
                    }

                    var target = targets.FirstOrDefault(candidate => reasons.ContainsKey(candidate.Target));
                    if (target.Target is not null)
                    {
                        reasons[caller] = $"calls {target.Name}";
                        changed = true;
                    }
                }
            }

            var findings = ImmutableArray.CreateBuilder<PrototypeFinding>();
            foreach (var (method, reason) in reasons)
            {
                if (method.ContainingType.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Temporalio.Workflows.WorkflowAttribute"))
                {
                    findings.Add(new PrototypeFinding(method.ToDisplayString(), reason));
                }
            }

            return findings.ToImmutable();
        }

        private static bool IsNondeterministicInvocation(IMethodSymbol method) =>
            method.Name == "Delay" && method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task";
    }
}
