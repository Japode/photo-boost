using System.Globalization;
using System.Windows;
using PhotoBoost.Imaging;
using PhotoBoost.Inference;
using PhotoBoost.Inference.Pipelines;

namespace PhotoBoost.App;

/// <summary>
/// The operations panel (PB24): runs an upscale on the inference queue (PB15) with tile progress
/// (PB20), a cancel, and the run logged (PB22) and summed up in the status bar.
/// </summary>
public partial class MainWindow
{
    private InferenceService? service;
    private ModelLibrary? library;
    private InferenceJob<RgbaImage>? running;
    private readonly OperationLog log = new(OperationLog.DefaultFolder);

    /// <summary>The last finished result, which export (PB25) writes; null until a run succeeds.</summary>
    public RgbaImage? Result { get; private set; }

    /// <summary>The variant the quality mode names: INT8 for Compact, FP32 for High (PB29 adds FP16).</summary>
    private string Variant => Options.Quality == QualityMode.High ? "fp32" : "int8";

    private int Factor => Scale2.IsChecked == true ? 2 : 4;

    /// <summary>Called once the provider is known: opens the models folder and the queue.</summary>
    private void StartOperations(ProviderSelection provider)
    {
        library = new ModelLibrary(Options.ModelsFolder, provider);
        service = new InferenceService();
        Closed += async (_, _) =>
        {
            running?.Cancel();
            await service.DisposeAsync().ConfigureAwait(false);
            library.Dispose();
        };
        Scale2.Checked += (_, _) => RefreshOperations();
        Scale4.Checked += (_, _) => RefreshOperations();
        RefreshOperations();
    }

    /// <summary>Enables Upscale only with an image, models on disk, and nothing running.</summary>
    private void RefreshOperations()
    {
        Operations.Visibility = Image is null ? Visibility.Collapsed : Visibility.Visible;
        if (library is null)
        {
            UpscaleButton.IsEnabled = false;
            return;
        }

        string model = Upscale.ModelFor(Factor);
        bool present = library.Has(model, Variant);
        OperationNote.Text = present ? string.Empty : $"{model} ({Variant}) is not in {Options.ModelsFolder}. Download the models first.";
        OperationNote.Visibility = present ? Visibility.Collapsed : Visibility.Visible;
        UpscaleButton.IsEnabled = Image is not null && present && running is null;
    }

    private async void OnUpscaleClick(object sender, RoutedEventArgs e)
    {
        if (Image is not { } source || service is null || library is null)
        {
            return;
        }

        int factor = Factor;
        Progress<JobProgress> progress = new(ShowProgress);
        running = UpscaleJob.Enqueue(service, library, source, factor, Variant, progress, log);
        JobProgress.Value = 0;
        JobStatus.Text = "Starting…";
        JobPanel.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        RefreshOperations();

        try
        {
            RgbaImage result = await running.Completion.ConfigureAwait(true);
            Result = result;
            ShowResult(result, factor);
            JobStatus.Text = "Done.";
        }
        catch (OperationCanceledException)
        {
            JobStatus.Text = "Cancelled.";
        }
        catch (Exception failure) when (failure is InvalidOperationException or InsufficientMemoryException or System.IO.IOException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
        {
            JobStatus.Text = failure.Message;
        }
        finally
        {
            running = null;
            CancelButton.IsEnabled = false;
            ShowLastRun();
            RefreshOperations();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        running?.Cancel();
        CancelButton.IsEnabled = false;
        JobStatus.Text = "Cancelling after this tile…";
    }

    private void ShowProgress(JobProgress p)
    {
        JobProgress.Maximum = Math.Max(1, p.Total);
        JobProgress.Value = p.Done;
        string left = p.Remaining is { } remaining && p.Done < p.Total
            ? $" · about {Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.CurrentCulture)} s left"
            : string.Empty;
        JobStatus.Text = $"Tile {p.Done} of {p.Total}{left}";
    }

    private void ShowResult(RgbaImage result, int factor)
    {
        Picture.Source = Preview.Bitmap(result);
        ImageInfo.Text = $"{System.IO.Path.GetFileName(ImagePath)} — upscaled {factor}× — {result.Width} × {result.Height}";
    }

    private void ShowLastRun()
    {
        if (log.ReadCurrent() is { Count: > 0 } records)
        {
            OperationRecord last = records[^1];
            string seconds = (last.Milliseconds / 1000).ToString("0.0", CultureInfo.CurrentCulture);
            LastRun.Text = $"{last.Operation}: {seconds} s on {last.Provider}{(last.Outcome == "ok" ? string.Empty : $" ({last.Outcome})")}";
            LastRunChip.Visibility = Visibility.Visible;
        }
    }
}
