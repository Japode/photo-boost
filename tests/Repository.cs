namespace PhotoBoost.Tests;

/// <summary>
/// The repository a test was built from, found once for every test assembly. A test that reads
/// a project file, a fixture or a golden image does it from here. Linked into every test project
/// from <c>tests/Directory.Build.props</c>.
/// </summary>
internal static class Repository
{
    private static readonly Lazy<string> Found = new(Walk);

    /// <summary>The folder holding <c>PhotoBoost.slnx</c>, above wherever the test runs from.</summary>
    /// <exception cref="DirectoryNotFoundException">The test is not running from inside the repository.</exception>
    public static string Root => Found.Value;

    private static string Walk()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoBoost.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException(
                   $"PhotoBoost.slnx is not above {AppContext.BaseDirectory}, so this test is not running "
                   + "from inside the repository it was built from.");
    }
}
