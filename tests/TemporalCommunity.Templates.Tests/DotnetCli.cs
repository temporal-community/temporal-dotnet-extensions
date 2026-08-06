using System.Diagnostics;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Runs a <c>dotnet</c> subprocess and fails loudly (with captured stdout/stderr) on a non-zero
/// exit code, rather than letting a silent failure surface only as a missing file later in a test.
/// </summary>
internal static class DotnetCli
{
    public static async Task<string> RunAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
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
