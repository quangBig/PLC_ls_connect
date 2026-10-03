using System.Windows;

namespace IndustrialVision.App.Views;

/// <summary>
/// MainWindow code-behind.
/// Contains NO hardware logic — only window lifecycle management.
/// All logic is in MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Dispose ViewModel if it implements IDisposable
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
