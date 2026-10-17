using System.Windows;
using System.Windows.Controls;
using MonitorCenter.Services;

namespace MonitorCenter.UI;

/// <summary>Shows the progress and result of a "Check for updates" click.</summary>
internal sealed class UpdateWindow : Window
{
    private const int NoBackdrop = 1;

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 8 };

    public UpdateWindow()
    {
        Title = "MonitorCenter updates";
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryForegroundBrush");
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        Content = panel;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowTheme(this, NoBackdrop);
    }

    public void Render(UpdateStatus status)
    {
        _status.Text = status.Message;
        _progress.IsIndeterminate = status.Stage == UpdateStage.Checking;
        _progress.Value = status.Progress;
        _progress.Visibility = status.Stage is UpdateStage.Checking or UpdateStage.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
