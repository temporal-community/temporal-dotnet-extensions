using BenchmarkDotNet.Attributes;
using FakeItEasy;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Benchmarks;

/// <summary>
/// Measures client construction through the generated registry and DispatchProxy compatibility
/// path. Network execution is intentionally excluded so the benchmark isolates library overhead.
/// </summary>
[MemoryDiagnoser]
public class ClientCreationBenchmarks
{
    private IDurableObjectFactory _factory = null!;
    private int _id;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton(A.Fake<ITemporalClient>());
        services.AddDurableObjects("benchmarks");
        _factory = services.BuildServiceProvider().GetRequiredService<IDurableObjectFactory>();
    }

    [Benchmark(Baseline = true)]
    public IGeneratedBenchmarkCounter GeneratedClient() =>
        _factory.Get<IGeneratedBenchmarkCounter>($"generated-{_id++}");

    [Benchmark]
    public IProxyBenchmarkCounter CompatibilityProxy() =>
        _factory.Get<IProxyBenchmarkCounter>($"proxy-{_id++}");

    // Nested contracts are deliberately not source-generated, preserving the compatibility path.
    [Workflow]
    public interface IProxyBenchmarkCounter : IDurableObject
    {
        [WorkflowUpdate]
        Task IncrementAsync();
    }
}

[Workflow]
public interface IGeneratedBenchmarkCounter : IDurableObject
{
    [WorkflowUpdate]
    Task IncrementAsync();
}
