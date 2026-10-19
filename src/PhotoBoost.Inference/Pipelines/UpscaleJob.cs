using PhotoBoost.Imaging;

namespace PhotoBoost.Inference.Pipelines;

/// <summary>An upscale as the UI asks for one: queued, reporting tile progress, cancellable (PB20).</summary>
public static class UpscaleJob
{
    /// <summary>The stage name tile progress carries.</summary>
    public const string Stage = "upscale";

    /// <summary>Queues an upscale behind whatever is running.</summary>
    /// <param name="service">The inference queue.</param>
    /// <param name="library">The models.</param>
    /// <param name="image">The image.</param>
    /// <param name="factor">2 or 4.</param>
    /// <param name="variant">fp32, fp16 or int8.</param>
    /// <param name="progress">Tile X of Y with an estimate of the time left; on the UI thread when made there.</param>
    /// <returns>The job; cancelling it stops after the current tile.</returns>
    public static InferenceJob<RgbaImage> Enqueue(InferenceService service, ModelLibrary library, RgbaImage image, int factor, string variant, IProgress<JobProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.Enqueue(
            $"upscale x{factor}",
            context => Upscale.Run(library, image, factor, variant, progress: (done, total) => context.Report(done, total, Stage)),
            progress);
    }
}
