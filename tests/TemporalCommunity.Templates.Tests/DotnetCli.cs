using System.ComponentModel;
using System.Diagnostics;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Runs a <c>dotnet</c> subprocess and fails loudly (with captured stdout/stderr) on a non-zero
/// exit code, rather than letting a silent failure surface only as a missing file later in a test.
/// </summary>
internal static class DotnetCli
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public static Task<string> RunAsync(string workingDirectory, params string[] arguments) =>
        RunAsync(workingDirectory, DefaultTimeout, arguments);

    internal static async Task<string> RunAsync(
        string workingDirectory,
        TimeSpan timeout,
        params string[] arguments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between timeout cancellation and Kill.
            }
            catch (Win32Exception) when (process.HasExited)
            {
                // Kill raced with process exit on platforms that report that race as Win32Exception.
            }

            string timedOutStdOut;
            string timedOutStdErr;
            try
            {
                await process.WaitForExitAsync().WaitAsync(CleanupTimeout).ConfigureAwait(false);
                var timedOutOutput = await Task.WhenAll(stdOutTask, stdErrTask)
                    .WaitAsync(CleanupTimeout)
                    .ConfigureAwait(false);
                timedOutStdOut = timedOutOutput[0];
                timedOutStdErr = timedOutOutput[1];
            }
            catch (TimeoutException cleanupException)
            {
                throw new TimeoutException(
                    $"'dotnet {string.Join(' ', arguments)}' in '{workingDirectory}' timed out after {timeout}, " +
                    $"and process cleanup did not complete within {CleanupTimeout}.",
                    cleanupException);
            }

            throw new TimeoutException(
                $"'dotnet {string.Join(' ', arguments)}' in '{workingDirectory}' did not exit within {timeout}." +
                $"{Environment.NewLine}stdout:{Environment.NewLine}{timedOutStdOut}" +
                $"{Environment.NewLine}stderr:{Environment.NewLine}{timedOutStdErr}");
        }

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet {string.Join(' ', arguments)}' in '{workingDirectory}' exited with code {process.ExitCode}." +
                $"{Environment.NewLine}stdout:{Environment.NewLine}{stdOut}" +
                $"{Environment.NewLine}stderr:{Environment.NewLine}{stdErr}");
        }

        return stdOut;
    }
}
