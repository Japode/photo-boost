using System.Windows;
using PhotoBoost.Inference;

namespace PhotoBoost.App;

/// <summary>The main window, empty until the open-image screen lands.</summary>
public partial class MainWindow : Window
{
    /// <summary>Builds the window from MainWindow.xaml and starts provider detection once it shows.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ThemeMode = Theme.Mode(Appearance.Default.Theme);
        Loaded += OnLoaded;
    }

    /// <summary>The provider every model session uses, once detection has finished (PB14).</summary>
    public ProviderSelection? Provider { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // A probe session takes a few hundred milliseconds on a GPU; the window does not wait.
        ProviderSelection selection = await Task.Run(() => ProviderDetector.Detect(ProviderPreference.Auto)).ConfigureAwait(true);
        Provider = selection;
        ProviderStatus.Text = selection.Label;
    }
}
