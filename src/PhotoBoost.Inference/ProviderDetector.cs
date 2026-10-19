using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoBoost.Inference;

/// <summary>Where inference runs.</summary>
public enum ExecutionProvider
{
    /// <summary>ONNX Runtime's CPU provider, always available.</summary>
    Cpu,

    /// <summary>DirectML on a DX12 GPU (Microsoft.ML.OnnxRuntime.DirectML, chosen by the PB5 spike).</summary>
    DirectML,
}

/// <summary>What the user or a command-line flag asked for.</summary>
public enum ProviderPreference
{
    /// <summary>The best that works: DirectML, then CPU.</summary>
    Auto,

    /// <summary>DirectML, falling back to CPU only if it cannot run.</summary>
    DirectML,

    /// <summary>CPU, without trying a GPU.</summary>
    Cpu,
}

/// <summary>The provider chosen, and why the others were not.</summary>
/// <param name="Provider">The provider every session will use.</param>
/// <param name="Adapter">The GPU, when the provider is DirectML.</param>
/// <param name="Fallback">Why a GPU was asked for and not used, or null.</param>
public sealed record ProviderSelection(ExecutionProvider Provider, GpuAdapter? Adapter, string? Fallback)
{
    /// <summary>What the status bar shows, e.g. "DirectML · NVIDIA GeForce RTX 4060, 8 GB".</summary>
    public string Label => Provider == ExecutionProvider.DirectML && Adapter is not null
        ? $"DirectML · {Adapter.Label}"
        : Fallback is null ? "CPU" : $"CPU (GPU unavailable: {Fallback})";
}

/// <summary>Runs the probe model on one provider; true when it ran, with the failure otherwise.</summary>
public interface IProviderProbe
{
    /// <summary>Tries a provider.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="adapter">The GPU for DirectML, null for CPU.</param>
    /// <param name="error">Why it failed, when it did.</param>
    /// <returns>Whether a session ran the probe model.</returns>
    bool TryRun(ExecutionProvider provider, GpuAdapter? adapter, out string? error);
}

/// <summary>
/// Picks the execution provider once at startup (PB14): DirectML on the GPU with the most video
/// memory when a probe session runs there, else CPU. Never throws for a GPU that will not run;
/// that is a fallback with its reason, shown to the user.
/// </summary>
public static class ProviderDetector
{
    /// <summary>Detects with the real GPUs and the real probe.</summary>
    /// <param name="preference">What was asked for.</param>
    /// <returns>The selection.</returns>
    public static ProviderSelection Detect(ProviderPreference preference) =>
        Detect(preference, GpuAdapters.Enumerate(), new OnnxRuntimeProbe());

    /// <summary>Detects against the given GPUs and probe.</summary>
    /// <param name="preference">What was asked for.</param>
    /// <param name="adapters">The GPUs to consider.</param>
    /// <param name="probe">How a provider is tried.</param>
    /// <returns>The selection.</returns>
    public static ProviderSelection Detect(ProviderPreference preference, IReadOnlyList<GpuAdapter> adapters, IProviderProbe probe)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(probe);

        string? fallback = null;
        if (preference != ProviderPreference.Cpu)
        {
            GpuAdapter? best = GpuAdapters.Best(adapters);
            if (best is null)
            {
                fallback = "no DirectX 12 GPU found";
            }
            else if (probe.TryRun(ExecutionProvider.DirectML, best, out string? error))
            {
                return new ProviderSelection(ExecutionProvider.DirectML, best, null);
            }
            else
            {
                fallback = error ?? "DirectML did not run";
            }
        }

        return probe.TryRun(ExecutionProvider.Cpu, null, out string? cpuError)
            ? new ProviderSelection(ExecutionProvider.Cpu, null, fallback)
            : throw new InvalidOperationException($"ONNX Runtime cannot run on the CPU: {cpuError}");
    }

    /// <summary>Reads a --provider value: auto, dml (or directml) or cpu.</summary>
    /// <param name="value">The flag's value.</param>
    /// <param name="preference">The preference it names.</param>
    /// <returns>Whether the value was one of those.</returns>
    public static bool TryParse(string? value, out ProviderPreference preference)
    {
        preference = value?.Trim().ToUpperInvariant() switch
        {
            "AUTO" => ProviderPreference.Auto,
            "DML" or "DIRECTML" or "GPU" => ProviderPreference.DirectML,
            "CPU" => ProviderPreference.Cpu,
            _ => (ProviderPreference)(-1),
        };
        return Enum.IsDefined(preference);
    }
}

/// <summary>Builds the SessionOptions every model session uses for a selection.</summary>
public static class SessionOptionsFactory
{
    /// <summary>Options for the selected provider; the caller disposes them.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="adapter">The GPU for DirectML.</param>
    /// <returns>The options.</returns>
    public static SessionOptions Create(ExecutionProvider provider, GpuAdapter? adapter)
    {
        SessionOptions options = new();
        try
        {
            if (provider == ExecutionProvider.DirectML)
            {
                // DirectML runs neither with memory patterns nor with parallel execution.
                options.EnableMemoryPattern = false;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.AppendExecutionProvider_DML(adapter?.Index ?? 0);
            }

            return options;
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }
}

/// <summary>Tries a provider by running the probe model through a real ONNX Runtime session.</summary>
public sealed class OnnxRuntimeProbe : IProviderProbe
{
    private static readonly byte[] Model = ProbeModel.Build();

    /// <inheritdoc/>
    public bool TryRun(ExecutionProvider provider, GpuAdapter? adapter, out string? error)
    {
        // The append is inside the try on purpose: the PB5 spike saw it throw 887A0004 on a GPU
        // that worked before and after, before any session existed.
        try
        {
            using SessionOptions options = SessionOptionsFactory.Create(provider, adapter);
            using InferenceSession session = new(Model, options);
            DenseTensor<float> input = new([1, 3, 16, 16]);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                session.Run([NamedOnnxValue.CreateFromTensor(session.InputNames[0], input)]);
            error = null;
            return results.Count == 1;
        }
        catch (OnnxRuntimeException failure)
        {
            error = failure.Message;
            return false;
        }
        catch (DllNotFoundException failure)
        {
            error = failure.Message;
            return false;
        }
    }
}
