using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PhotoBoost.Imaging;

namespace PhotoBoost.App;

/// <summary>
/// The export card (PB25): a format, a quality for the lossy ones, a background when a transparent
/// image goes to JPEG, and Save as…; --export writes after every finished run without a dialog.
/// What is written is the result, or the opened image itself before any run.
/// </summary>
public partial class MainWindow
{
    private static readonly ImageFormat[] Formats = [ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Webp];

    private ImageFormat SelectedFormat => Formats[Math.Max(0, ExportFormat.SelectedIndex)];

    private RgbaImage? Exportable => Image;

    /// <summary>Shows the quality slider and the transparency warning only where they apply.</summary>
    private void RefreshExport()
    {
        if (QualityRow is null)
        {
            return; // Called while InitializeComponent is still building the card.
        }

        QualityRow.Visibility = SelectedFormat == ImageFormat.Png ? Visibility.Collapsed : Visibility.Visible;
        bool loses = Exportable is { } image && ImageEncoder.LosesTransparency(image, SelectedFormat);
        TransparencyRow.Visibility = loses ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = Exportable is not null;
    }

    private void OnExportOptionChanged(object sender, SelectionChangedEventArgs e) => RefreshExport();

    private EncodeOptions OptionsFor(ImageFormat format) => new(
        format,
        Quality: (int)ExportQuality.Value,
        Background: ExportBackground.SelectedIndex == 1 ? Brand.JpegNight : Brand.JpegPaper);

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (Exportable is null)
        {
            return;
        }

        ImageFormat format = SelectedFormat;
        string extension = format switch { ImageFormat.Jpeg => ".jpg", ImageFormat.Webp => ".webp", _ => ".png" };
        SaveFileDialog dialog = new()
        {
            Title = "Export",
            Filter = $"{format} image|*{extension}",
            DefaultExt = extension,
            FileName = Path.GetFileNameWithoutExtension(ImagePath) + (Result is null ? string.Empty : "-photoboost") + extension,
        };
        if (dialog.ShowDialog(this) == true)
        {
            await SaveAsync(dialog.FileName, OptionsFor(format)).ConfigureAwait(true);
        }
    }

    /// <summary>--export: writes after a run, in the format its extension names, with the card's quality.</summary>
    private async Task ExportAfterRunAsync()
    {
        if (Options.Export is not { } path)
        {
            return;
        }

        if (ImageEncoder.FormatFor(path) is not { } format)
        {
            ShowExportStatus($"--export {Path.GetFileName(path)}: use .png, .jpg or .webp.");
            return;
        }

        await SaveAsync(path, OptionsFor(format)).ConfigureAwait(true);
    }

    private async Task SaveAsync(string path, EncodeOptions options)
    {
        RgbaImage image = Exportable!;
        string full = Path.GetFullPath(path);
        try
        {
            await Task.Run(() => ImageEncoder.Save(image, full, options)).ConfigureAwait(true);
            ShowExportStatus($"Saved {Path.GetFileName(full)}");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowExportStatus($"Could not save {Path.GetFileName(full)}: {failure.Message}");
        }
    }

    private void ShowExportStatus(string text)
    {
        ExportStatus.Text = text;
        ExportStatus.Visibility = Visibility.Visible;
    }
}
