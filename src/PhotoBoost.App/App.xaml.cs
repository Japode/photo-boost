using System.Windows;

namespace PhotoBoost.App;

/// <summary>The application: parses the command line (PB39) and opens the main window with it.</summary>
public partial class App : Application
{
    /// <summary>The exit code for a command line that was refused.</summary>
    public const int BadCommandLine = 2;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        ParsedCommandLine parsed = CommandLine.Parse(e.Args);
        if (!parsed.Ok)
        {
            MessageBox.Show(parsed.Error + "\n\nSee docs/COMMAND-LINE.md.", "PhotoBoost", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(BadCommandLine);
            return;
        }

        MainWindow window = new(parsed.Options!);
        MainWindow = window;
        window.Show();
    }
}
