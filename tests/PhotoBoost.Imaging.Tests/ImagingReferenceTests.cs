using System.Reflection;

namespace PhotoBoost.Imaging.Tests;

/// <summary>
/// PB2: Imaging is unit-testable on its own only while it loads no UI and no ONNX Runtime
/// assembly. A project reference is checked by the architecture tests; this reads what the
/// compiled assembly references, which a package reference changes too.
/// </summary>
public sealed class ImagingReferenceTests
{
    private static readonly string[] Forbidden =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "Microsoft.ML.OnnxRuntime",
        "PhotoBoost.Inference",
        "PhotoBoost.Render",
    ];

    [Fact]
    public void ReferencesNoUiAndNoInferenceAssembly()
    {
        string[] referenced = Assembly.Load("PhotoBoost.Imaging")
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        string[] found = referenced.Intersect(Forbidden, StringComparer.Ordinal).ToArray();

        Assert.True(found.Length == 0, $"PhotoBoost.Imaging references {string.Join(", ", found)}.");
    }
}
