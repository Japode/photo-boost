using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using PhotoBoost.Inference;

namespace PhotoBoost.App;

/// <summary>
/// The High-quality models block of the About panel (PB13): fetches the optional FP32 set from
/// the manifest PB12 published into the folder downloads go to, with progress, a cancel, and
/// every file checked against its SHA-256 before anything loads it. The installer's FP16 set
/// keeps working whether or not this ever runs.
/// </summary>
public partial class MainWindow
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private CancellationTokenSource? downloading;

    /// <summary>Says whether the FP32 set is here, and on the CPU why it is worth having.</summary>
    private void RefreshDownloads()
    {
        bool complete = HighQualityInstalled();
        string cpu = Provider?.Provider == ExecutionProvider.Cpu
            ? " This PC runs on the CPU, where they are also about twice as fast as the standard ones."
            : string.Empty;
        DownloadNote.Text = complete
            ? "The high-quality (FP32) models are installed."
            : $"The high-quality (FP32) models are an optional download of up to 690 MB; files already here are not fetched again.{cpu}";
        DownloadModelsButton.IsEnabled = downloading is null && !complete;
    }

    /// <summary>Whether every installed model has its FP32 variant on disk.</summary>
    private bool HighQualityInstalled()
    {
        if (library is not null)
        {
            return Complete(library.Installed());
        }

        using ModelLibrary reading = new(Options.ModelFolders, new ProviderSelection(ExecutionProvider.Cpu, null, null));
        return Complete(reading.Installed());

        static bool Complete(IReadOnlyList<InstalledModel> installed) =>
            installed.Count > 0 && installed.Where(m => m.Meta.Variants.ContainsKey(ModelDownloads.Variant)).All(m => m.Variants.Contains(ModelDownloads.Variant));
    }

    private async void OnDownloadModelsClick(object sender, RoutedEventArgs e)
    {
        downloading = new CancellationTokenSource();
        DownloadModelsButton.IsEnabled = false;
        CancelDownloadButton.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadStatus.Text = "Connecting…";
        try
        {
            ModelManifest manifest = ModelDownloads.Embedded;
            Progress<DownloadProgress> progress = new(p =>
            {
                DownloadProgressBar.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
                DownloadStatus.Text = string.Create(CultureInfo.CurrentCulture, $"{p.File}: {p.Done / 1048576.0:0} of {p.Total / 1048576.0:0} MB");
            });
            IReadOnlyList<string> elsewhere = [.. Options.ModelFolders.Where(folder => folder != Options.ModelsFolder)];
            DownloadResult result = await ModelDownloads.DownloadAsync(Http, manifest, Options.ModelsFolder, progress, downloading.Token, elsewhere).ConfigureAwait(true);
            DownloadStatus.Text = result.Fetched == 0
                ? "Nothing to download: every high-quality model was already here."
                : $"Downloaded and verified {result.Fetched} file(s).";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus.Text = "Cancelled. The files already verified stay; the next download goes on from there.";
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            DownloadStatus.Text = $"The download stopped: {failure.Message}";
        }
        finally
        {
            downloading.Dispose();
            downloading = null;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            RefreshDownloads();
            FillAbout();
            RefreshOperations();
        }
    }

    private void OnCancelDownloadClick(object sender, RoutedEventArgs e) => downloading?.Cancel();
}
