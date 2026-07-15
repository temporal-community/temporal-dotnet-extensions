using Temporalio.Workflows;

Console.WriteLine("Temporal analyzer project sample");

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
        lock (this)                                      // TEMP011
        {
            if (Monitor.TryEnter(this))                 // TEMP011
            {
                Monitor.Exit(this);                      // TEMP011
            }
        }
        Console.WriteLine("workflow output");           // TEMP013
        return id.ToString();
    }
}
