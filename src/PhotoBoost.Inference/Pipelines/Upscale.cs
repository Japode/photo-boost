using Microsoft.ML.OnnxRuntime;
using PhotoBoost.Imaging;

namespace PhotoBoost.Inference.Pipelines;

/// <summary>
/// Real-ESRGAN upscaling, 2x or 4x (PB19), tiled through <see cref="Tiler"/> so a large photo
/// stays within memory. 2x runs the native x2plus model rather than x4 and a downscale: the same
/// download size, trained for the factor, and a quarter of x4's activation memory per pixel.
/// </summary>
public static class Upscale
{
    /// <summary>The 4x model's folder name.</summary>
    public const string X4 = "realesrgan-x4plus";

    /// <summary>The 2x model's folder name.</summary>
    public const string X2 = "realesrgan-x2plus";

    /// <summary>
    /// Context read around each tile: 32, not the tiler's generic 16, because RRDBNet sees further.
    /// Measured on 0014.jpg in 64 px tiles against one whole-image run (PB19): x4 gave 31.3, 36.2,
    /// 40.0, 45.0 and 58.3 dB at overlaps 8, 16, 24, 32 and 48; x2 gave 31.1, 33.6, 37.5, 39.9 and
    /// 43.1 dB. At 32 the seams sit under INT8's own 33 dB loss, for 1.56x the work of a bare
    /// 256 px tile, where 48 would cost 1.89x.
    /// </summary>
    public const int DefaultOverlap = 32;

    /// <summary>The model that upscales by a factor.</summary>
    /// <param name="factor">2 or 4.</param>
    /// <returns>Its folder name.</returns>
    public static string ModelFor(int factor) => factor switch
    {
        2 => X2,
        4 => X4,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Upscaling is 2x or 4x."),
    };

    /// <summary>Upscales an image; call it from the inference queue, one run at a time.</summary>
    /// <param name="library">The models and their sessions.</param>
    /// <param name="image">The image.</param>
    /// <param name="factor">2 or 4.</param>
    /// <param name="variant">fp32, fp16 or int8.</param>
    /// <param name="tile">The tile side, or null for the model's recommendation.</param>
    /// <param name="progress">Called after each tile with (done, total); may throw to cancel.</param>
    /// <param name="overlap">Context read around each tile, or null for <see cref="DefaultOverlap"/>.</param>
    /// <param name="log">Told each recovery step (smaller tiles, CPU fallback; PB21).</param>
    /// <param name="stats">Filled with the tile plan and recovery steps, for the operation log (PB22).</param>
    /// <returns>The image, factor times larger, alpha resized bilinearly.</returns>
    public static RgbaImage Run(ModelLibrary library, RgbaImage image, int factor, string variant, int? tile = null, Action<int, int>? progress = null, int? overlap = null, Action<string>? log = null, UpscaleStats? stats = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(image);
        string model = ModelFor(factor);
        ModelMeta meta = library.Meta(model);
        if (meta.Scale != factor)
        {
            throw new InvalidDataException($"{model}'s meta.json says x{meta.Scale}, not x{factor}.");
        }

        using SessionLease<InferenceSession> lease = library.Sessions.Acquire(new ModelKey(model, variant));
        TileOptions options = new(
            Scale: factor,
            Low: (float)meta.Input.ValueRange[0],
            High: (float)meta.Input.ValueRange[1],
            Tile: tile ?? meta.Tile.Recommended ?? Tiler.DefaultTile,
            Overlap: overlap ?? DefaultOverlap,
            Multiple: meta.Input.Multiple);

        // PB21: an out-of-memory tile is retried smaller down to 64 px, then the run moves to the
        // CPU, whose session is only loaded if that ever happens.
        SessionLease<InferenceSession>? cpuLease = null;
        try
        {
            TileModel? fallback = library.CpuFallback is { } cpu
                ? (input, width, height) => RunTile((cpuLease ??= cpu.Acquire(new ModelKey(model, variant))).Session, meta, input, width, height)
                : null;
            if (stats is not null)
            {
                stats.Model = model;
                stats.Tile = options.Tile;
            }

            TileRetry retry = new(MemoryFailures.Classify, MinTile: MinTile, Fallback: fallback, Log: line =>
            {
                stats?.Retries.Add(line);
                log?.Invoke(line);
            });

            RgbaImage result = Tiler.Run(
                image,
                (input, width, height) => RunTile(lease.Session, meta, input, width, height),
                options,
                (done, total) =>
                {
                    stats?.Tiles = done;
                    progress?.Invoke(done, total);
                },
                retry);
            if (image.HasTransparency)
            {
                AlphaResampler.ScaleInto(image, result, factor);
            }

            return result;
        }
        finally
        {
            cpuLease?.Dispose();
        }
    }

    /// <summary>The smallest tile an out-of-memory retry halves down to before moving to the CPU.</summary>
    public const int MinTile = 64;

    private static float[] RunTile(InferenceSession session, ModelMeta meta, float[] input, int width, int height)
    {
        using OrtValue tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, height, width]);
        using RunOptions run = new();
        using IDisposableReadOnlyCollection<OrtValue> outputs = session.Run(run, [meta.Input.Name], [tensor], [meta.Output.Name]);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }
}
