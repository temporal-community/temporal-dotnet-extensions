namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Copies the checked-in fixture host project to a fresh temp directory per test. Tests must never
/// instantiate templates directly into the checked-in copy under <c>Fixtures/HostProject</c> —
/// parallel test runs would collide and mutate the worktree.
/// </summary>
internal static class TestFixtures
{
    public static string CopyHostProjectToTempDirectory()
    {
        var destination = CreateTempDirectory();
        CopyDirectory(RepoPaths.FixtureHostProjectDirectory, destination);
        return destination;
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "temporal-templates-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leaked scratch dir under the OS temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; a leaked scratch dir under the OS temp folder is harmless.
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)));
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            var name = Path.GetFileName(directoryPath);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopyDirectory(directoryPath, Path.Combine(destinationDirectory, name));
        }
    }
}
