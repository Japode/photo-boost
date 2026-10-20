using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoBoost.Inference;

/// <summary>One file the manifest lists: where it is, and what it must be.</summary>
/// <param name="Url">A resolve/&lt;commit&gt;/ URL, so its bytes cannot change under the app.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="Sha256">Its SHA-256, lowercase hex.</param>
public sealed record ManifestFile(string Url, long Bytes, string Sha256);

/// <summary>One model the manifest lists.</summary>
/// <param name="Name">Its folder name.</param>
/// <param name="Meta">Its meta.json.</param>
/// <param name="Variants">Its ONNX files, by variant.</param>
/// <param name="License">Its license, as the About panel shows it.</param>
public sealed record ManifestModel(string Name, ManifestFile Meta, IReadOnlyDictionary<string, ManifestFile> Variants, string License);

/// <summary>scripts/build-manifest.py's manifest.json (PB12), schema 1.</summary>
/// <param name="Schema">The format version.</param>
/// <param name="Version">The manifest's own version.</param>
/// <param name="Models">The models it lists.</param>
public sealed record ModelManifest(int Schema, string Version, IReadOnlyList<ManifestModel> Models)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Reads a manifest and refuses one this build cannot trust.</summary>
    /// <param name="json">manifest.json's text.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InvalidDataException">It is not schema 1, or a URL is not https.</exception>
    public static ModelManifest Parse(string json)
    {
        ModelManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModelManifest>(json, Options) ?? throw new InvalidDataException("The manifest is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"The manifest is not valid: {error.Message}", error);
        }

        if (manifest.Schema != 1)
        {
            throw new InvalidDataException($"The manifest is schema {manifest.Schema}; this build reads 1.");
        }

        if (manifest.Models.SelectMany(m => m.Variants.Values.Append(m.Meta)).FirstOrDefault(f => !f.Url.StartsWith("https://", StringComparison.Ordinal)) is { } plain)
        {
            throw new InvalidDataException($"The manifest lists {plain.Url}, which is not https.");
        }

        return manifest;
    }
}

/// <summary>How far a download is, for the progress bar.</summary>
/// <param name="File">The file being fetched, e.g. gfpgan-v1.4/fp32.onnx.</param>
/// <param name="Done">Bytes fetched across the whole download.</param>
/// <param name="Total">Bytes the whole download needs.</param>
public readonly record struct DownloadProgress(string File, long Done, long Total);

/// <summary>What a download did.</summary>
/// <param name="Fetched">Files fetched and verified.</param>
/// <param name="Present">Files already on disk with the right bytes.</param>
public sealed record DownloadResult(int Fetched, int Present);

/// <summary>
/// The optional FP32 set (PB13), fetched from PB12's manifest at alegauss/photo-boost-models into
/// the folder downloads go to. Each file is streamed to a .part file, checked against the
/// manifest's size and SHA-256, and only then renamed into place, so ONNX Runtime never loads a
/// file that arrived corrupt; one that fails is deleted and the download stops, naming it. A
/// file already present with the right bytes is not fetched again, which is also how a download
/// cancelled half way resumes on the next try.
/// </summary>
public static class ModelDownloads
{
    /// <summary>
    /// The manifest this build pins, scripts/published-models.json's manifest_url: a
    /// resolve/&lt;commit&gt;/ URL, so the files it lists cannot change under a released app.
    /// </summary>
    public const string ManifestUrl = "https://huggingface.co/alegauss/photo-boost-models/resolve/f1964dae136379fdfc4a4f021f6f07102a333f0f/manifest.json";

    /// <summary>The variant the download fetches.</summary>
    public const string Variant = "fp32";

    /// <summary>Bytes the variant needs across the manifest.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The total, meta.json files included.</returns>
    public static long TotalBytes(ModelManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Models.Sum(m => m.Meta.Bytes + (m.Variants.TryGetValue(Variant, out ManifestFile? f) ? f.Bytes : 0));
    }

    /// <summary>Whether every file the variant needs is on disk with the right bytes.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="root">The folder downloads go to.</param>
    /// <returns>True when nothing is left to fetch.</returns>
    /// <param name="elsewhere">Other models folders whose identical file counts, such as the installer's.</param>
    public static bool Complete(ModelManifest manifest, string root, IReadOnlyList<string>? elsewhere = null) =>
        Files(manifest, root).All(file => Present(file.Path, file.Label, file.Expected, elsewhere));

    /// <summary>Bytes still to fetch: what <see cref="DownloadAsync"/> would download now.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="root">The folder downloads go to.</param>
    /// <param name="elsewhere">Other models folders whose identical file counts.</param>
    /// <returns>The bytes.</returns>
    public static long MissingBytes(ModelManifest manifest, string root, IReadOnlyList<string>? elsewhere = null) =>
        Files(manifest, root).Where(file => !Present(file.Path, file.Label, file.Expected, elsewhere)).Sum(file => file.Expected.Bytes);

    /// <summary>Reads the pinned manifest.</summary>
    /// <param name="http">The client.</param>
    /// <param name="cancellation">Cancels the read.</param>
    /// <returns>The manifest.</returns>
    public static async Task<ModelManifest> FetchManifestAsync(HttpClient http, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(http);
        string json = await http.GetStringAsync(new Uri(ManifestUrl), cancellation).ConfigureAwait(false);
        return ModelManifest.Parse(json);
    }

    /// <summary>Fetches every file the variant needs that is not on disk with the right bytes.</summary>
    /// <param name="http">The client.</param>
    /// <param name="manifest">The manifest.</param>
    /// <param name="root">The folder downloads go to, e.g. %LOCALAPPDATA%\PhotoBoost\models.</param>
    /// <param name="progress">Told after each chunk.</param>
    /// <param name="cancellation">Cancels between chunks; the .part file is deleted.</param>
    /// <param name="elsewhere">
    /// Other models folders: a file already there with the manifest's bytes is not fetched, so
    /// GFPGAN's FP32, which the installer carries since PB54, is not downloaded a second time.
    /// </param>
    /// <returns>How many files were fetched and how many were already there.</returns>
    /// <exception cref="InvalidDataException">A file arrived with another size or SHA-256; it was deleted.</exception>
    public static async Task<DownloadResult> DownloadAsync(HttpClient http, ModelManifest manifest, string root, IProgress<DownloadProgress>? progress, CancellationToken cancellation, IReadOnlyList<string>? elsewhere = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(manifest);
        long total = TotalBytes(manifest);
        long done = 0;
        int fetched = 0, present = 0;
        foreach ((string path, string label, ManifestFile expected) in Files(manifest, root))
        {
            if (Present(path, label, expected, elsewhere))
            {
                present++;
                done += expected.Bytes;
                progress?.Report(new DownloadProgress(label, done, total));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string part = path + ".part";
            long before = done;
            try
            {
                using HttpResponseMessage response = await http.GetAsync(new Uri(expected.Url), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using Stream source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await using (FileStream target = new(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    byte[] buffer = new byte[1 << 20];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                        done += read;
                        progress?.Report(new DownloadProgress(label, done, total));
                    }
                }

                if (!Valid(part, expected))
                {
                    throw new InvalidDataException($"{label} arrived different from the manifest (size or SHA-256), so it was deleted and nothing was loaded.");
                }

                File.Move(part, path, overwrite: true);
                fetched++;
                done = before + expected.Bytes;
            }
            catch
            {
                File.Delete(part);
                throw;
            }
        }

        return new DownloadResult(fetched, present);
    }

    private static IEnumerable<(string Path, string Label, ManifestFile Expected)> Files(ModelManifest manifest, string root)
    {
        foreach (ManifestModel model in manifest.Models)
        {
            if (model.Variants.TryGetValue(Variant, out ManifestFile? onnx))
            {
                yield return (Path.Combine(root, model.Name, Variant + ".onnx"), $"{model.Name}/{Variant}.onnx", onnx);
            }

            // After the ONNX file, so a model folder never holds a meta.json without its variant.
            yield return (Path.Combine(root, model.Name, "meta.json"), $"{model.Name}/meta.json", model.Meta);
        }
    }

    private static bool Present(string path, string label, ManifestFile expected, IReadOnlyList<string>? elsewhere) =>
        Valid(path, expected) || (elsewhere ?? []).Any(other => Valid(Path.Combine(other, label), expected));

    private static bool Valid(string path, ManifestFile expected)
    {
        FileInfo file = new(path);
        if (!file.Exists || file.Length != expected.Bytes)
        {
            return false;
        }

        using FileStream stream = file.OpenRead();
        return Convert.ToHexStringLower(SHA256.HashData(stream)) == expected.Sha256;
    }
}
