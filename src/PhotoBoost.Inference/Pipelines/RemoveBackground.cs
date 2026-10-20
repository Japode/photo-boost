using Microsoft.ML.OnnxRuntime;
using PhotoBoost.Imaging;

namespace PhotoBoost.Inference.Pipelines;

/// <summary>
/// Background removal with RMBG-1.4 (PB32), as BRIA's own example does it: the photo is resized to
/// the model's square, ignoring its aspect, the mask that comes back is resized to the photo and
/// stretched so its lowest value is transparent and its highest opaque, and it becomes the alpha.
/// Colour is left alone; an alpha the photo already had is kept where it is lower.
/// </summary>
public static class RemoveBackground
{
    /// <summary>The model's folder name.</summary>
    public const string Model = "rmbg-1.4";

    /// <summary>The stage name its one step reports under.</summary>
    public const string Stage = "background";

    /// <summary>Removes a photo's background; call it from the inference queue.</summary>
    /// <param name="library">The models and their sessions.</param>
    /// <param name="image">The photo, left as it is.</param>
    /// <param name="variant">fp32, fp16 or int8.</param>
    /// <returns>The photo with the background transparent.</returns>
    public static RgbaImage Run(ModelLibrary library, RgbaImage image, string variant)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(image);
        ModelMeta meta = library.Meta(Model);
        if (meta.Input.Size is not int side || meta.Output.Channels != 1)
        {
            throw new InvalidDataException($"{Model}'s meta.json does not describe a fixed-size mask model.");
        }

        using SessionLease<InferenceSession> lease = library.Sessions.Acquire(new ModelKey(Model, variant));
        return Matte(image, side, meta, input => RunModel(lease.Session, meta, input, side));
    }

    /// <summary>
    /// The model-agnostic half, so it can be tested with a stand-in: resize, run, and the mask
    /// brought back as alpha.
    /// </summary>
    /// <param name="image">The photo, left as it is.</param>
    /// <param name="side">The model's square input side.</param>
    /// <param name="meta">The model's description, for its value ranges.</param>
    /// <param name="model">1 x 3 x side x side in, side x side mask values out.</param>
    /// <returns>A copy of the photo with the mask as its alpha.</returns>
    public static RgbaImage Matte(RgbaImage image, int side, ModelMeta meta, Func<float[], float[]> model)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(model);
        float[] input = new float[3 * side * side];
        RgbaImage small = ImageResampler.Resize(image, side, side);
        TensorConversion.ToTensor(small, small.Bounds, (float)meta.Input.ValueRange[0], (float)meta.Input.ValueRange[1], input);
        float[] mask = ImageResampler.ResizePlane(model(input), side, side, image.Width, image.Height);

        // BRIA's postprocess: the mask stretched to its own range. A flat mask says nothing about
        // where the subject is, so it is read in the model's range instead of divided by zero.
        float low = mask.Min();
        float high = mask.Max();
        if (high - low < 1e-6f)
        {
            low = (float)meta.Output.ValueRange[0];
            high = (float)meta.Output.ValueRange[1];
        }

        RgbaImage result = new(image.Width, image.Height, image.SourceBitDepth);
        image.Samples.CopyTo(result.Samples);
        for (int y = 0; y < image.Height; y++)
        {
            Span<ushort> row = result.Row(y);
            for (int x = 0; x < image.Width; x++)
            {
                float t = Math.Clamp((mask[(y * image.Width) + x] - low) / (high - low), 0f, 1f);
                ushort alpha = (ushort)MathF.Round(t * RgbaImage.Max);
                row[(x * 4) + 3] = Math.Min(row[(x * 4) + 3], alpha);
            }
        }

        return result;
    }

    private static float[] RunModel(InferenceSession session, ModelMeta meta, float[] input, int side)
    {
        using OrtValue tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, side, side]);
        using RunOptions run = new();
        using IDisposableReadOnlyCollection<OrtValue> outputs = session.Run(run, [meta.Input.Name], [tensor], [meta.Output.Name]);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }
}
