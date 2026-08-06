namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Locates paths relative to the repo root so tests can run against the staged (unpacked)
/// template content directly, without a pack/install round-trip.
/// </summary>
internal static class RepoPaths
{
    private static readonly string RootDirectory = FindRepoRoot();

    public static string ContentRoot(string templateFolderName) =>
        Path.Combine(RootDirectory, "templates", "TemporalCommunity.Templates", "content", templateFolderName);

    public static string FixtureHostProjectDirectory =>
        Path.Combine(RootDirectory, "tests", "TemporalCommunity.Templates.Tests", "Fixtures", "HostProject");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TemporalDurableObjects.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (TemporalDurableObjects.slnx) above '{AppContext.BaseDirectory}'.");
    }
}
