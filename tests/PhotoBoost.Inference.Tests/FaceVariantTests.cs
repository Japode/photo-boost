using PhotoBoost.Inference.Pipelines;

namespace PhotoBoost.Inference.Tests;

/// <summary>PB54: GFPGAN runs FP32 on a GPU whenever it is on disk, since its FP16 is a flat square on DirectML.</summary>
public sealed class FaceVariantTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pb-faces-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(ExecutionProvider.Cpu, new[] { "fp16", "fp32" }, "fp16", "fp16")]
    [InlineData(ExecutionProvider.DirectML, new[] { "fp16", "fp32" }, "fp16", "fp32")]
    [InlineData(ExecutionProvider.WindowsML, new[] { "fp16", "fp32" }, "fp16", "fp32")]
    [InlineData(ExecutionProvider.DirectML, new[] { "fp16" }, "fp16", "fp16")]
    [InlineData(ExecutionProvider.Cpu, new[] { "fp32" }, "fp16", "fp32")]
    [InlineData(ExecutionProvider.DirectML, new[] { "fp16", "fp32", "int8" }, "int8", "int8")]
    public void TheRestorerRunsTheVariantItCanRunRight(ExecutionProvider provider, string[] onDisk, string asked, string expected)
    {
        string folder = Path.Combine(root, FaceRestore.Model);
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(Repository.Root, "scripts", "meta.example.json"), Path.Combine(folder, "meta.json"));
        foreach (string variant in onDisk)
        {
            File.WriteAllBytes(Path.Combine(folder, variant + ".onnx"), [0]);
        }

        GpuAdapter? adapter = provider == ExecutionProvider.Cpu ? null : new GpuAdapter(0, "GPU", 8L << 30);
        string? accelerator = provider == ExecutionProvider.WindowsML ? "NvTensorRTRTXExecutionProvider" : null;
        using ModelLibrary library = new([root], new ProviderSelection(provider, adapter, null, accelerator), engines: new EngineCache(Path.Combine(root, "engines")));

        Assert.Equal(expected, FaceRestore.VariantFor(library, asked));
    }
}
