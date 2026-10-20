using System.Windows;
using PhotoBoost.Inference;

namespace PhotoBoost.App;

/// <summary>The main window, empty until the open-image screen lands.</summary>
public partial class MainWindow : Window
{
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // A probe session takes a few hundred milliseconds on a GPU; the window does not wait.
        ProviderSelection selection = await Task.Run(() => ProviderDetector.Detect(Options.Provider)).ConfigureAwait(true);
        Provider = selection;
        ProviderStatus.Text = selection.Label;
    }
}
