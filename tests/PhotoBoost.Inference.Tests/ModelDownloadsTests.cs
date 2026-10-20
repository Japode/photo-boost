using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoBoost.Inference.Tests;

/// <summary>
/// PB13: the FP32 set is fetched from the manifest through .part files, checked by SHA-256 before
/// it is renamed into place, and a file already here, or in the installer's folder, is not fetched.
/// </summary>
public sealed class ModelDownloadsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pb-downloads-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> served = new(StringComparer.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EveryFileIsFetchedVerifiedAndPutInItsModelFolder()
    {
        ModelManifest manifest = Manifest(("tiny-x4", "weights"u8.ToArray()));
        using HttpClient http = new(new Served(served));

        DownloadResult result = await ModelDownloads.DownloadAsync(http, manifest, Path.Combine(root, "models"), null, TestContext.Current.CancellationToken);

        Assert.Equal(new DownloadResult(2, 0), result);
        Assert.Equal("weights"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(root, "models", "tiny-x4", "fp32.onnx"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(root, "models", "tiny-x4", "meta.json")));
        Assert.Empty(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories));
        Assert.True(ModelDownloads.Complete(manifest, Path.Combine(root, "models")));
    }

    [Fact]
    public async Task AFileThatArrivesDifferentIsDeletedAndNamed()
    {
        ModelManifest manifest = Manifest(("tiny-x4", "weights"u8.ToArray()));
        served["https://host/tiny-x4-fp32.onnx"] = "tampered"u8.ToArray();
        using HttpClient http = new(new Served(served));

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ModelDownloads.DownloadAsync(http, manifest, Path.Combine(root, "models"), null, TestContext.Current.CancellationToken));

        Assert.Contains("tiny-x4/fp32.onnx", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AFileAlreadyHereOrInTheInstallersFolderIsNotFetchedAgain()
    {
        ModelManifest manifest = Manifest(("tiny-x4", "weights"u8.ToArray()), ("tiny-face", "face"u8.ToArray()));
        string bundled = Path.Combine(root, "bundled");
        Directory.CreateDirectory(Path.Combine(bundled, "tiny-face"));
        await File.WriteAllBytesAsync(Path.Combine(bundled, "tiny-face", "fp32.onnx"), "face"u8.ToArray(), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(bundled, "tiny-face", "meta.json"), served["https://host/tiny-face-meta.json"], TestContext.Current.CancellationToken);
        using HttpClient http = new(new Served(served));
        string models = Path.Combine(root, "models");

        Assert.Equal("weights"u8.Length + served["https://host/tiny-x4-meta.json"].Length, ModelDownloads.MissingBytes(manifest, models, [bundled]));
        DownloadResult first = await ModelDownloads.DownloadAsync(http, manifest, models, null, TestContext.Current.CancellationToken, [bundled]);
        DownloadResult again = await ModelDownloads.DownloadAsync(http, manifest, models, null, TestContext.Current.CancellationToken, [bundled]);

        Assert.Equal(new DownloadResult(2, 2), first);
        Assert.Equal(new DownloadResult(0, 4), again);
        Assert.False(Directory.Exists(Path.Combine(models, "tiny-face")));
    }

    [Fact]
    public async Task ACancelledDownloadLeavesNoPartialFile()
    {
        ModelManifest manifest = Manifest(("tiny-x4", new byte[3 << 20]));
        using CancellationTokenSource cancel = new();
        using HttpClient http = new(new Served(served));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ModelDownloads.DownloadAsync(http, manifest, Path.Combine(root, "models"), new SynchronousProgress(cancel), cancel.Token));

        Assert.Empty(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(root, "models", "tiny-x4", "fp32.onnx")));
    }

    [Fact]
    public void AManifestOfAnotherSchemaOrWithAPlainHttpUrlIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => ModelManifest.Parse("""{"schema": 2, "version": "1", "models": []}"""));
        Assert.Throws<InvalidDataException>(() => ModelManifest.Parse(
            """{"schema": 1, "version": "1", "models": [{"name": "m", "meta": {"url": "http://x/m.json", "bytes": 1, "sha256": "00"}, "variants": {}, "license": "l"}]}"""));
    }

    [Fact]
    public void ThePinnedManifestIsTheOnePublished()
    {
        // scripts/publish-models.py writes published-models.json; the app pins what it says.
        using JsonDocument record = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "scripts", "published-models.json")));
        Assert.Equal(ModelDownloads.ManifestUrl, record.RootElement.GetProperty("manifest_url").GetString());
        Assert.Contains("/resolve/", ModelDownloads.ManifestUrl, StringComparison.Ordinal);
    }

    private ModelManifest Manifest(params (string Name, byte[] Weights)[] models)
    {
        List<object> entries = [];
        foreach ((string name, byte[] weights) in models)
        {
            byte[] meta = System.Text.Encoding.UTF8.GetBytes($$"""{"name": "{{name}}"}""");
            served[$"https://host/{name}-fp32.onnx"] = weights;
            served[$"https://host/{name}-meta.json"] = meta;
            entries.Add(new
            {
                name,
                meta = Listed(meta, $"https://host/{name}-meta.json"),
                variants = new Dictionary<string, object> { ["fp32"] = Listed(weights, $"https://host/{name}-fp32.onnx") },
                license = "test",
            });
        }

        return ModelManifest.Parse(JsonSerializer.Serialize(new { schema = 1, version = "1", models = entries }));
    }

    private static object Listed(byte[] bytes, string url) =>
        new { url, bytes = bytes.LongLength, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };

    private sealed class Served(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out byte[]? bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // Progress<T> posts to the thread pool; this reports inline, so the cancel lands mid-file.
    private sealed class SynchronousProgress(CancellationTokenSource cancel) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => cancel.Cancel();
    }
}
