using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Client.Interceptors;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.Benchmarks;

/// <summary>
/// Compares query client dispatch with an immediately completed outbound interceptor.
/// Includes the common SDK client path, but no server, network, payload conversion, or replay.
/// </summary>
[MemoryDiagnoser]
public class QueryDispatchBenchmarks
{
    private ServiceProvider _services = null!;
    private IGeneratedBenchmarkReader _generated = null!;
    private GeneratedBenchmarkReaderDurableObjectClient _asyncGenerated = null!;
    private ICompatibilityBenchmarkReader _proxy = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Queries terminate at the interceptor, so this lazy client never opens a connection.
        var temporal = TemporalClient.CreateLazy(new TemporalClientConnectOptions("unused:7233")
        {
            Interceptors = [new CompletedQueryInterceptor()],
        });
        _services = new ServiceCollection().AddSingleton<ITemporalClient>(temporal)
            .AddDurableObjects("query-dispatch-benchmarks").BuildServiceProvider();
        var factory = _services.GetRequiredService<IDurableObjectFactory>();
        _generated = factory.Get<IGeneratedBenchmarkReader>("reader");
        _asyncGenerated = (GeneratedBenchmarkReaderDurableObjectClient)_generated;
        _proxy = factory.Get<ICompatibilityBenchmarkReader>("reader");
        if (_generated.Read() != 42 || _proxy.Read() != 42)
            throw new InvalidOperationException("Benchmark transport setup failed.");
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();

    [Benchmark(Baseline = true)]
    public int GeneratedSynchronousQuery() => _generated.Read();

    [Benchmark]
    public int CompatibilitySynchronousQuery() => _proxy.Read();

    [Benchmark]
    public Task<int> GeneratedAsynchronousQuery() => _asyncGenerated.ReadAsync();

    // Nested contracts deliberately retain the DispatchProxy compatibility path.
    [Workflow]
    public interface ICompatibilityBenchmarkReader : IDurableObject
    {
        [WorkflowQuery("Read")] int Read();
    }

    private sealed class CompletedQueryInterceptor : IClientInterceptor
    {
        public ClientOutboundInterceptor InterceptClient(ClientOutboundInterceptor nextInterceptor) =>
            new CompletedQueryOutbound(nextInterceptor);
    }

    private sealed class CompletedQueryOutbound(ClientOutboundInterceptor next) : ClientOutboundInterceptor(next)
    {
        private static readonly Task<int> Result = Task.FromResult(42);

        public override Task<TResult> QueryWorkflowAsync<TResult>(QueryWorkflowInput input) =>
            typeof(TResult) == typeof(int) && input.Query == "Read"
                ? (Task<TResult>)(object)Result
                : throw new InvalidOperationException("The benchmark only supports integer Read queries.");
    }
}

[Workflow]
public interface IGeneratedBenchmarkReader : IDurableObject
{
    [WorkflowQuery("Read")] int Read();
}
