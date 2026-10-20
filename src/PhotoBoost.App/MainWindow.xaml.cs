using System.IO;
using System.Windows;
using Microsoft.Win32;
using PhotoBoost.Imaging;
using PhotoBoost.Inference;

namespace PhotoBoost.App;

/// <summary>The main window: the open screen (PB23), and the provider in the status bar (PB14).</summary>
public partial class MainWindow : Window
{
    private const string OpenFilter =
        "Images|*.png;*.jpg;*.jpeg;*.webp;*.tif;*.tiff;*.bmp;*.heic;*.heif;*.avif;*.jxr|All files|*.*";
    /// <summary>Builds the window from MainWindow.xaml and starts provider detection once it shows.</summary>
    /// <param name="options">What the command line asked for (PB39).</param>
    public MainWindow(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        InitializeComponent();
        ThemeMode = Theme.Mode(Appearance.Default.Theme);
        Loaded += OnLoaded;
    }

    /// <summary>What the command line asked for; the screens that open, export and pick quality read it.</summary>
    public AppOptions Options { get; }

    /// <summary>The provider every model session uses, once detection has finished (PB14).</summary>
    public ProviderSelection? Provider { get; private set; }

    /// <summary>The version shown now (PB28), which operations and export work on; null on the drop zone.</summary>
    public RgbaImage? Image { get; private set; }

    /// <summary>The file it came from.</summary>
    public string? ImagePath { get; private set; }

    /// <summary>
    /// Decodes a file off the UI thread and shows it, or says why not on the drop zone. Every way in
    /// arrives here: the button, a drop from Explorer, and --open (PB39), which is how cases come.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>When the file is shown or refused.</returns>
    public async Task OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        string name = Path.GetFileName(full);
        RgbaImage image;
        try
        {
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"{name} was not found.", full);
            }

            image = await Task.Run(() => ImageDecoder.Decode(full)).ConfigureAwait(true);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowError(failure is InvalidDataException ? $"{name} is not an image Windows can open." : failure.Message);
            return;
        }

        ImagePath = full;

        // A screen reader, and a case, hears the photo by its file name rather than an empty image.
        System.Windows.Automation.AutomationProperties.SetName(Picture, name);
        OpenError.Visibility = Visibility.Collapsed;
        DropZone.Visibility = Visibility.Collapsed;
        Viewer.Visibility = Visibility.Visible;
        history.Reset(image, $"Opened {name}");
        ShowVersion(image);
    }

    private void ShowError(string message)
    {
        OpenError.Text = message;
        OpenError.Visibility = Visibility.Visible;
        DropZone.Visibility = Visibility.Visible;
        Viewer.Visibility = Visibility.Collapsed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Options.Open is { } open)
        {
            await OpenAsync(open).ConfigureAwait(true);
        }

        // A probe session takes a few hundred milliseconds on a GPU; the window does not wait.
        ProviderSelection selection = await Task.Run(() => ProviderDetector.Detect(Options.Provider)).ConfigureAwait(true);
        Provider = selection;
        ProviderStatus.Text = selection.Label;
        StartOperations(selection);
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Filter = OpenFilter, Title = "Open a photo" };
        if (dialog.ShowDialog(this) == true)
        {
            await OpenAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await OpenAsync(files[0]).ConfigureAwait(true);
        }
    }
}
