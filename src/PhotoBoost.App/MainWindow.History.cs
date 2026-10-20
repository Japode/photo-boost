using System.Windows;
using System.Windows.Input;
using PhotoBoost.Imaging;

namespace PhotoBoost.App;

/// <summary>
/// The history (PB28): every operation's result is a version; undo and redo move between them,
/// and operations and export always work on the one shown.
/// </summary>
public partial class MainWindow
{
    private readonly ImageHistory history = new();

    /// <summary>The label of the version shown, e.g. "Upscale 4×".</summary>
    public string? VersionLabel => history.Position < 0 ? null : history.Labels[history.Position];

    /// <summary>Records a finished operation as the new current version and shows it.</summary>
    private void PushVersion(RgbaImage image, string label)
    {
        history.Push(image, label);
        ShowVersion(image);
    }

    /// <summary>Shows a version: picture, caption, history list, and what the panel can do with it.</summary>
    private void ShowVersion(RgbaImage image)
    {
        Image = image;
        Picture.Source = Preview.Bitmap(image);
        Backdrop.Background = image.HasTransparency ? Preview.Checkerboard() : null;
        string name = System.IO.Path.GetFileName(ImagePath) ?? string.Empty;
        ImageInfo.Text = history.Position > 0
            ? $"{name} — {VersionLabel} — {image.Width} × {image.Height}"
            : $"{name} — {image.Width} × {image.Height}";
        // Numbered, so two "Upscale 2×" steps are two items: WPF keys a list's items, and their
        // automation peers, by value, and equal strings collapse into one.
        HistoryList.ItemsSource = history.Labels.Select((label, i) => $"{i + 1}. {label}").ToList();
        HistoryList.SelectedIndex = history.Position;
        CommandManager.InvalidateRequerySuggested();
        RefreshOperations();
    }

    private void CanUndo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = running is null && history.CanUndo;

    private void CanRedo(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = running is null && history.CanRedo;

    private void OnUndo(object sender, ExecutedRoutedEventArgs e)
    {
        if (history.Undo() is { } image)
        {
            ShowVersion(image);
        }
    }

    private void OnRedo(object sender, ExecutedRoutedEventArgs e)
    {
        if (history.Redo() is { } image)
        {
            ShowVersion(image);
        }
    }
}
