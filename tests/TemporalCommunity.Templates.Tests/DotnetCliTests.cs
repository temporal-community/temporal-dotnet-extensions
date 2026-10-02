using Xunit;

namespace TemporalCommunity.Templates.Tests;

public sealed class DotnetCliTests
{
    [Fact]
    public async Task RunAsyncTerminatesTimedOutProcess()
    {
        var workingDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var programPath = Path.Combine(workingDirectory, "Program.cs");
            var pidPath = Path.Combine(workingDirectory, "child.pid");
            await File.WriteAllTextAsync(
                programPath,
                $$"""
                await File.WriteAllTextAsync(args[0], Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Task.Delay(Timeout.InfiniteTimeSpan);
                """);

            var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
                DotnetCli.RunAsync(
                    workingDirectory,
                    TimeSpan.FromSeconds(2),
                    programPath,
                    pidPath));

            Assert.Contains("did not exit within", exception.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(pidPath), "The timed-out child did not record its process ID.");
            var childPid = int.Parse(await File.ReadAllTextAsync(pidPath), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(
                await WaitForProcessExitAsync(childPid),
                $"Timed-out child process {childPid} is still running.");
        }
        finally
        {
            TestFixtures.DeleteDirectory(workingDirectory);
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        return false;
    }
}
