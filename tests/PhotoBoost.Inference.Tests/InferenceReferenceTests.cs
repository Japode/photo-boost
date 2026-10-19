using System.Reflection;

namespace PhotoBoost.Inference.Tests;

/// <summary>
/// PB2: the inference queue runs off the UI thread and is tested without a window, so the
/// compiled assembly may reference no WPF and no display assembly, by project or by package.
/// </summary>
public sealed class InferenceReferenceTests
{
    private static readonly string[] Forbidden =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "PhotoBoost.Render",
        "PhotoBoost",
    ];

    [Fact]
    public void ReferencesNoUiAndNoDisplayAssembly()
    {
        string[] referenced = Assembly.Load("PhotoBoost.Inference")
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        string[] found = referenced.Intersect(Forbidden, StringComparer.Ordinal).ToArray();

        Assert.True(found.Length == 0, $"PhotoBoost.Inference references {string.Join(", ", found)}.");
    }
}
