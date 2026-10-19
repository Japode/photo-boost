using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;

namespace PhotoBoost.Inference;

/// <summary>
/// The models folder as the pipelines use it (PB19): &lt;root&gt;/&lt;model&gt;/meta.json beside the
/// ONNX variants, and one session cache on the detected provider.
/// </summary>
public sealed class ModelLibrary : IDisposable
{
    private readonly ConcurrentDictionary<string, ModelMeta> metas = new(StringComparer.Ordinal);
    private readonly Lazy<SessionCache<InferenceSession>>? cpu;

    /// <summary>Opens a models folder.</summary>
    /// <param name="root">The folder holding one subfolder per model.</param>
    /// <param name="provider">Where sessions run.</param>
    /// <param name="idle">How long an unused session is kept.</param>
    public ModelLibrary(string root, ProviderSelection provider, TimeSpan? idle = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Root = root;
        Provider = provider;
        TimeSpan keep = idle ?? SessionCache<InferenceSession>.DefaultIdle;
        Sessions = ModelSessions.Create(root, provider, keep);
        if (provider.Provider != ExecutionProvider.Cpu)
        {
            cpu = new(() => ModelSessions.Create(root, new ProviderSelection(ExecutionProvider.Cpu, null, null), keep));
        }
    }

    /// <summary>Where <see cref="Sessions"/> run.</summary>
    public ProviderSelection Provider { get; }

    /// <summary>CPU sessions for a GPU run that ran out of memory (PB21); null when already on CPU.</summary>
    public SessionCache<InferenceSession>? CpuFallback => cpu?.Value;

    /// <summary>The models folder.</summary>
    public string Root { get; }

    /// <summary>The sessions, loaded on demand.</summary>
    public SessionCache<InferenceSession> Sessions { get; }

    /// <summary>Whether a model's variant is on disk.</summary>
    /// <param name="model">The model's folder name.</param>
    /// <param name="variant">fp32, fp16 or int8.</param>
    /// <returns>True when both its meta.json and its ONNX file exist.</returns>
    public bool Has(string model, string variant) =>
        File.Exists(Path.Combine(Root, model, "meta.json")) && File.Exists(Path.Combine(Root, model, variant + ".onnx"));

    /// <summary>A model's meta.json, read once.</summary>
    /// <param name="model">The model's folder name.</param>
    /// <returns>Its description.</returns>
    public ModelMeta Meta(string model) => metas.GetOrAdd(model, name => ModelMeta.Load(Path.Combine(Root, name, "meta.json")));

    /// <summary>Releases every session.</summary>
    public void Dispose()
    {
        Sessions.Dispose();
        if (cpu is { IsValueCreated: true })
        {
            cpu.Value.Dispose();
        }
    }
}
