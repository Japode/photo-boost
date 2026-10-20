using System.Windows;
using PhotoBoost.Imaging;
using PhotoBoost.Inference;
using PhotoBoost.Inference.Pipelines;

namespace PhotoBoost.App;

/// <summary>
/// The Remove background card (PB32): RMBG-1.4's mask becomes the image's alpha, on the same queue,
/// progress card and log as the other operations. PNG, the export default, keeps it.
/// </summary>
public partial class MainWindow
{
    /// <summary>Enables Remove background only with an image, its model, and nothing running.</summary>
    private void RefreshBackground()
    {
        string? missing = Missing((RemoveBackground.Model, Variant));
        BackgroundNote.Text = missing ?? string.Empty;
        BackgroundNote.Visibility = missing is null ? Visibility.Collapsed : Visibility.Visible;
        RemoveBackgroundButton.IsEnabled = Image is not null && missing is null && !Busy;
    }

    private async void OnRemoveBackgroundClick(object sender, RoutedEventArgs e)
    {
        if (Image is not { } source || service is null || library is null)
        {
            return;
        }

        bool refine = RefineEdges.IsChecked == true;
        running = RemoveBackgroundJob.Enqueue(service, library, source, Variant, refine, new Progress<JobProgress>(ShowProgress), log);
        JobProgress.Value = 0;
        JobStatus.Text = "Finding the subject…";
        JobPanel.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        RefreshOperations();

        try
        {
            RgbaImage result = await running.Completion.ConfigureAwait(true);
            running = null;
            PushVersion(result, refine ? "Remove background" : "Remove background (unrefined)");
            JobStatus.Text = "Done.";
            await ExportAfterRunAsync().ConfigureAwait(true);
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
}
