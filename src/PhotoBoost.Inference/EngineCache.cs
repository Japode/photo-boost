using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Vortice.DXGI;
using WinML = Microsoft.Windows.AI.MachineLearning;

namespace PhotoBoost.Inference;

/// <summary>
/// Compiled engines for a Windows ML provider (PB52), as ONNX Runtime EP context models: TensorRT-RTX
/// builds its engine for 10 to 17 s whenever a session is created, and loading the context model
/// it wrote takes 1.6 to 2.3 s instead (Real-ESRGAN x4 FP16 on the RTX 4060, 2026-10-20). Each
/// engine is keyed by everything that makes it stale: the model file, the provider package and its
/// version, the GPU and its driver, and the ONNX Runtime version. A key that no longer matches is a
/// new file, so a stale engine is never loaded, only left behind.
/// </summary>
public sealed class EngineCache(string folder)
{
    /// <summary>%LOCALAPPDATA%\PhotoBoost\engines.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoBoost", "engines");

    /// <summary>The folder the context models are written to.</summary>
    public string Folder { get; } = folder;

    /// <summary>
    /// The context model's name for one model file on one device: &lt;model&gt;-&lt;variant&gt;-&lt;key&gt;_ctx.onnx,
    /// the model being the file's folder and the variant its name.
    /// </summary>
    /// <param name="modelPath">The ONNX file.</param>
    /// <param name="key">What the engine depends on, from <see cref="Key"/>.</param>
    /// <returns>The path in <see cref="Folder"/>.</returns>
    public string PathFor(string modelPath, string key)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        string model = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(modelPath))) ?? "model";
        string variant = Path.GetFileNameWithoutExtension(modelPath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(Folder, $"{model}-{variant}-{Convert.ToHexStringLower(hash)[..16]}_ctx.onnx");
    }

    /// <summary>Everything a compiled engine depends on, as one string.</summary>
    /// <param name="modelPath">The ONNX file; its size and time stand for its content.</param>
    /// <param name="provider">The provider's ONNX Runtime name.</param>
    /// <param name="package">The provider package's full name, which carries its version.</param>
    /// <param name="device">The GPU, e.g. its LUID.</param>
    /// <param name="driver">The GPU driver's version.</param>
    /// <param name="runtime">ONNX Runtime's version.</param>
    /// <returns>The key.</returns>
    public static string Key(string modelPath, string provider, string package, string device, string driver, string runtime)
    {
        FileInfo file = new(modelPath);
        return string.Join(
            '|',
            Path.GetFullPath(modelPath),
            file.Length.ToString(CultureInfo.InvariantCulture),
            file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            provider,
            package,
            device,
            driver,
            runtime);
    }

    /// <summary>
    /// A session on the selection's Windows ML provider: from the context model when this key has
    /// one, else built from the model, which also writes the context model for next time.
    /// </summary>
    /// <param name="modelPath">The ONNX file.</param>
    /// <param name="selection">A Windows ML selection.</param>
    /// <returns>The session; the caller disposes it.</returns>
    public InferenceSession Open(string modelPath, ProviderSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        string context = PathFor(modelPath, Key(modelPath, selection.Accelerator ?? string.Empty, Package(selection), Device(selection), Driver(selection), OrtEnv.Instance().GetVersionString()));
        if (File.Exists(context))
        {
            try
            {
                using SessionOptions cached = SessionOptionsFactory.Create(selection);
                return new InferenceSession(context, cached);
            }
            catch (OnnxRuntimeException)
            {
                // A file the provider no longer reads: build it again below.
                File.Delete(context);
            }
        }

        Directory.CreateDirectory(Folder);
        string partial = context[..^".onnx".Length] + ".partial.onnx";
        using SessionOptions options = SessionOptionsFactory.Create(selection);
        options.AddSessionConfigEntry("ep.context_enable", "1");
        options.AddSessionConfigEntry("ep.context_file_path", partial);
        options.AddSessionConfigEntry("ep.context_embed_mode", "1");
        InferenceSession session = new(modelPath, options);
        if (File.Exists(partial))
        {
            File.Move(partial, context, overwrite: true);
        }

        return session;
    }

    private static string Package(ProviderSelection selection)
    {
        try
        {
            return WinML.ExecutionProviderCatalog.GetDefault().FindAllProviders()
                .FirstOrDefault(provider => provider.Name == selection.Accelerator)?.PackageId?.FullName ?? string.Empty;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return string.Empty;
        }
    }

    private static string Device(ProviderSelection selection) =>
        selection.Adapter is { } adapter ? string.Create(CultureInfo.InvariantCulture, $"{adapter.Index}:{adapter.VendorId}:{adapter.Name}") : string.Empty;

    // The user-mode driver's version, which changes when the GPU driver does.
    private static string Driver(ProviderSelection selection)
    {
        if (selection.Adapter is not { } adapter || DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null)
        {
            return string.Empty;
        }

        using (factory)
        {
            if (factory.EnumAdapters1((uint)adapter.Index, out IDXGIAdapter1? found).Failure || found is null)
            {
                return string.Empty;
            }

            using (found)
            {
                return found.CheckInterfaceSupport<IDXGIDevice>(out long version)
                    ? version.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }
        }
    }
}
