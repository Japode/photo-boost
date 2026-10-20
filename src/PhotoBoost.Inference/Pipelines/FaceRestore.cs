using Microsoft.ML.OnnxRuntime;
using PhotoBoost.Imaging;

namespace PhotoBoost.Inference.Pipelines;

/// <summary>What a face restore made.</summary>
/// <param name="Image">The photo with its faces restored, upscaled first when asked.</param>
/// <param name="Faces">How many faces were found and restored; 0 leaves the photo's faces as they were.</param>
public sealed record FaceRestoreResult(RgbaImage Image, int Faces);

/// <summary>
/// GFPGAN v1.4 face restoration (PB31), the way facexlib's FaceRestoreHelper does it: YuNet finds
/// the faces (PB30), each is aligned into a 512 crop, restored, and pasted back through the
/// inverse with a feathered mask. The background can be upscaled with Real-ESRGAN first, in which
/// case faces are still found on the original, where the detector sees them at the size it was
/// trained on, and their landmarks scaled up.
/// </summary>
public static class FaceRestore
{
    /// <summary>The restorer's folder name.</summary>
    public const string Model = "gfpgan-v1.4";

    /// <summary>The stage name the face steps report under; an upscale first reports as <see cref="UpscaleJob.Stage"/>.</summary>
    public const string Stage = "faces";

    /// <summary>Restores the faces in a photo; call it from the inference queue.</summary>
    /// <param name="library">The models and their sessions.</param>
    /// <param name="image">The photo, left as it is.</param>
    /// <param name="variant">GFPGAN's variant: fp32, fp16 or int8. The detector always runs fp32.</param>
    /// <param name="strength">How much of the restored face replaces the original, 0 to 1.</param>
    /// <param name="upscale">1 to keep the size, or 2 or 4 to upscale with Real-ESRGAN first.</param>
    /// <param name="progress">Called with (stage, done, total) after each tile and each face; may throw to cancel.</param>
    /// <returns>The restored photo and how many faces it had.</returns>
    public static FaceRestoreResult Run(ModelLibrary library, RgbaImage image, string variant, double strength = 1, int upscale = 1, Action<string, int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(image);
        ModelMeta meta = library.Meta(Model);
        if (meta.Input.Size is not int side)
        {
            throw new InvalidDataException($"{Model}'s meta.json does not give a fixed crop size.");
        }

        IReadOnlyList<Face> found = FaceDetection.Run(library, image);
        RgbaImage target = upscale == 1
            ? Copy(image)
            : Upscale.Run(library, image, upscale, variant, progress: (done, total) => progress?.Invoke(UpscaleJob.Stage, done, total));
        Face[] faces = [.. found.Select(face => Scaled(face, target.Width / (double)image.Width))];

        using SessionLease<InferenceSession> lease = library.Sessions.Acquire(new ModelKey(Model, variant));
        Restore(target, faces, side, meta, crop => RunCrop(lease.Session, meta, crop, side), strength, (done, total) => progress?.Invoke(Stage, done, total));
        return new(target, faces.Length);
    }

    /// <summary>
    /// The per-face half, with the model as a function so it can be tested without GFPGAN: each
    /// face is cropped, turned into the model's tensor, restored, and pasted back in place.
    /// </summary>
    /// <param name="image">The photo, changed in place.</param>
    /// <param name="faces">The faces, in the photo's coordinates.</param>
    /// <param name="side">The crop's side.</param>
    /// <param name="meta">The restorer's description, for its value ranges.</param>
    /// <param name="model">1 x 3 x side x side in, the same out.</param>
    /// <param name="strength">How much of the restored face shows, 0 to 1.</param>
    /// <param name="progress">Called with (done, total) after each face; may throw to cancel.</param>
    public static void Restore(RgbaImage image, IReadOnlyList<Face> faces, int side, ModelMeta meta, Func<float[], float[]> model, double strength, Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(model);
        PixelRect whole = new(0, 0, side, side);
        float[] input = new float[3 * side * side];
        for (int i = 0; i < faces.Count; i++)
        {
            Similarity toCrop = FaceAlignment.ToCrop(faces[i], side);
            RgbaImage crop = FaceAlignment.Crop(image, toCrop, side);
            TensorConversion.ToTensor(crop, whole, (float)meta.Input.ValueRange[0], (float)meta.Input.ValueRange[1], input);
            TensorConversion.FromTensor(model(input), (float)meta.Output.ValueRange[0], (float)meta.Output.ValueRange[1], crop, whole);
            FaceAlignment.PasteBack(image, crop, toCrop, strength);
            progress?.Invoke(i + 1, faces.Count);
        }
    }

    private static float[] RunCrop(InferenceSession session, ModelMeta meta, float[] input, int side)
    {
        using OrtValue tensor = OrtValue.CreateTensorValueFromMemory(input, [1, 3, side, side]);
        using RunOptions run = new();
        using IDisposableReadOnlyCollection<OrtValue> outputs = session.Run(run, [meta.Input.Name], [tensor], [meta.Output.Name]);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    private static Face Scaled(Face face, double k) => k == 1 ? face : face with
    {
        X = face.X * k,
        Y = face.Y * k,
        Width = face.Width * k,
        Height = face.Height * k,
        Landmarks = [.. face.Landmarks.Select(p => new PointD(p.X * k, p.Y * k))],
    };

    private static RgbaImage Copy(RgbaImage image)
    {
        RgbaImage copy = new(image.Width, image.Height, image.SourceBitDepth);
        image.Samples.CopyTo(copy.Samples);
        return copy;
    }
}
