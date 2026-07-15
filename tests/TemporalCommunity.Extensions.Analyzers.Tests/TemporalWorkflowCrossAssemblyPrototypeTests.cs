using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace TemporalCommunity.Extensions.Analyzers.Tests;

public sealed class TemporalWorkflowCrossAssemblyPrototypeTests
{
    private const string WorkflowAttribute = """
        namespace Temporalio.Workflows
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WorkflowAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void PropagatesSummaryFromReferencedAssembly()
    {
        var helperCompilation = CreateCompilation(
            "Helpers",
            """
            namespace Helpers;

            public static class Clock
            {
                public static System.DateTime Read() => System.DateTime.UtcNow;
            }
            """);
        using var image = new MemoryStream();
        Assert.True(helperCompilation.Emit(image).Success);
        image.Position = 0;

        var summaries = Summarize(helperCompilation);
        var workflowCompilation = CreateCompilation(
            "WorkflowAssembly",
            """
            using Helpers;

            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.Threading.Tasks.Task<System.DateTime> RunAsync() =>
                    System.Threading.Tasks.Task.FromResult(Clock.Read());
            }
            """,
            MetadataReference.CreateFromStream(image));

        var findings = AnalyzeWorkflows(workflowCompilation, summaries);

        var finding = Assert.Single(findings);
        Assert.Equal("MyWorkflow.RunAsync()", finding.MethodName);
        Assert.Contains("Helpers.Clock.Read()", finding.Reason, StringComparison.Ordinal);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        PortableExecutableReference? additionalReference = null)
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(WorkflowAttribute),
            CSharpSyntaxTree.ParseText(source),
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        if (additionalReference is not null)
        {
            references.Add(additionalReference);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private static ImmutableDictionary<string, string> Summarize(Compilation compilation)
    {
        var summaries = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol method)
                {
                    continue;
                }

                foreach (var memberAccess in declaration.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(memberAccess).Symbol is IPropertySymbol property &&
                        property.ContainingType.ToDisplayString() == "System.DateTime" &&
                        property.Name is "Now" or "UtcNow")
                    {
                        summaries[method.OriginalDefinition.ToDisplayString()] =
                            $"accesses System.DateTime.{property.Name}";
                    }
                }
            }
        }

        return summaries.ToImmutable();
    }

    private static ImmutableArray<PrototypeFinding> AnalyzeWorkflows(
        Compilation compilation,
        ImmutableDictionary<string, string> summaries)
    {
        var findings = ImmutableArray.CreateBuilder<PrototypeFinding>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol method ||
                    !method.ContainingType.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Temporalio.Workflows.WorkflowAttribute"))
                {
                    continue;
                }

                foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol target &&
                        summaries.TryGetValue(target.OriginalDefinition.ToDisplayString(), out var reason))
                    {
                        findings.Add(new PrototypeFinding(method.ToDisplayString(),
                            $"calls {target.OriginalDefinition.ToDisplayString()}: {reason}"));
                    }
                }
            }
        }

        return findings.ToImmutable();
    }

    private sealed record PrototypeFinding(string MethodName, string Reason);
}
