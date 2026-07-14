#:property EnableNETAnalyzers=false
#:property GenerateDocumentationFile=false
#:property NoWarn=CS1591
#:package Temporalio@1.15.0
// Replace the package version below with the version you want to evaluate.
#:package TemporalCommunity.Extensions.Analyzers@0.3.2

using Temporalio.Workflows;

Console.WriteLine("Temporal analyzer file-based sample");

[Workflow]
internal sealed class AnalyzerSampleWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false); // TEMP001
        await Task.Delay(100);                          // TEMP002
        _ = DateTime.UtcNow;                             // TEMP003
        await Task.Run(() => Task.CompletedTask);       // TEMP004
        Thread.Sleep(100);                               // TEMP007
        var id = Guid.NewGuid();                         // TEMP008
        _ = Random.Shared;                               // TEMP008
        return id.ToString();
    }
}
