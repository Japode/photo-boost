using System.Windows.Input;
using PhotoBoost.Imaging;

namespace PhotoBoost.App;

/// <summary>
/// The toolbar's basic edits, the quarter turns (PB65) and the flips (PB66): each is an exact move of the pixels, done on the UI thread
/// since it takes well under a second, and lands as a version in the history (PB28) like an
/// operation's result. They wait for a running operation as undo does.
/// </summary>
public partial class MainWindow
{
    /// <summary>A quarter turn clockwise, Ctrl+R.</summary>
    public static readonly RoutedUICommand RotateRightCommand = new(
        "Rotate right", "RotateRight", typeof(MainWindow), [new KeyGesture(Key.R, ModifierKeys.Control)]);

    /// <summary>A quarter turn counter-clockwise, Ctrl+Shift+R.</summary>
    public static readonly RoutedUICommand RotateLeftCommand = new(
        "Rotate left", "RotateLeft", typeof(MainWindow), [new KeyGesture(Key.R, ModifierKeys.Control | ModifierKeys.Shift)]);

    /// <summary>A mirror left to right; no shortcut, as in most editors.</summary>
    public static readonly RoutedUICommand FlipHorizontalCommand = new("Flip horizontal", "FlipHorizontal", typeof(MainWindow));

    /// <summary>A mirror top to bottom.</summary>
    public static readonly RoutedUICommand FlipVerticalCommand = new("Flip vertical", "FlipVertical", typeof(MainWindow));

    private void CanEdit(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Image is not null && !Busy;

    private void OnRotateRight(object sender, ExecutedRoutedEventArgs e) => Edit(ImageTransforms.RotateRight, "Rotate right");

    private void OnRotateLeft(object sender, ExecutedRoutedEventArgs e) => Edit(ImageTransforms.RotateLeft, "Rotate left");

    private void OnFlipHorizontal(object sender, ExecutedRoutedEventArgs e) => Edit(ImageTransforms.FlipHorizontal, "Flip horizontal");

    private void OnFlipVertical(object sender, ExecutedRoutedEventArgs e) => Edit(ImageTransforms.FlipVertical, "Flip vertical");

    private void Edit(Func<RgbaImage, RgbaImage> edit, string label)
    {
        if (Image is not { } image || Busy)
        {
            return;
        }

        // PB61: the last run's outcome describes a version that is no longer the newest.
        ClearRunStatus();
        PushVersion(edit(image), label);
    }
}
