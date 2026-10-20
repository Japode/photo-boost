using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;

namespace PhotoBoost.Inference;

/// <summary>A model on disk, as the About panel lists it (PB46).</summary>
/// <param name="Name">Its folder name.</param>
/// <param name="Meta">Its meta.json.</param>
/// <param name="Variants">The variants whose ONNX files are present, e.g. fp32 and int8.</param>
public sealed record InstalledModel(string Name, ModelMeta Meta, IReadOnlyList<string> Variants);

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

    /// <summary>
    /// Every model in the folder with a meta.json this build can read, by name, with the variants
    /// on disk (PB46: what the About panel lists, licenses included). A folder whose meta.json is
    /// unreadable is left out rather than failing the list.
    /// </summary>
    /// <returns>The models, by folder name.</returns>
    public IReadOnlyList<InstalledModel> Installed()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        List<InstalledModel> found = [];
        foreach (string folder in Directory.EnumerateDirectories(Root).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(folder);
            if (!File.Exists(Path.Combine(folder, "meta.json")))
            {
                continue;
            }

            try
            {
                ModelMeta meta = Meta(name);
                string[] onDisk = [.. meta.Variants.Keys.Where(variant => File.Exists(Path.Combine(folder, variant + ".onnx"))).Order(StringComparer.Ordinal)];
                found.Add(new(name, meta, onDisk));
            }
            catch (InvalidDataException)
            {
                // Listed nowhere: the operation that needs it says why when asked.
            }
        }

        return found;
    }

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
