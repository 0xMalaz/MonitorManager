using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MonitorCenter.Services;

namespace MonitorCenter.UI;

/// <summary>
/// "Report a problem": collects the diagnostics report, shows it, and sends it with the user's description when they
/// click Send. A report that can't be sent is saved to a file instead.
/// </summary>
internal sealed class ReportProblemWindow : Window
{
    private const int NoBackdrop = 1;

    private readonly Func<CancellationToken, Task<string>> _collect;
    private readonly Func<string, string, CancellationToken, Task> _send;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _description = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 80,
        MaxLength = 2000,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 6, 0, 16)
    };
    private readonly TextBox _report = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.NoWrap,
        Height = 240,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 11,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 6, 0, 12)
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly Button _sendButton = new() { Content = Strings.SendReport, MinWidth = 96, IsEnabled = false };
    private readonly Button _closeButton = new() { Content = Strings.Cancel, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
    private string? _collected;

    /// <param name="collect">Collects the diagnostics report.</param>
    /// <param name="send">Sends the description and the report; throws when it can't.</param>
    public ReportProblemWindow(
        Func<CancellationToken, Task<string>> collect,
        Func<string, string, CancellationToken, Task> send)
    {
        _collect = collect;
        _send = send;

        Title = Strings.ReportProblem;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryForegroundBrush");
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _sendButton.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        _sendButton.Click += async (_, _) => await SendAsync();
        _closeButton.Click += (_, _) => Close();
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");

        var intro = new TextBlock { Text = Strings.ReportProblemIntro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_closeButton);
        buttons.Children.Add(_sendButton);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(intro);
        panel.Children.Add(new TextBlock { Text = Strings.ReportProblemDescription });
        panel.Children.Add(_description);
        panel.Children.Add(new TextBlock { Text = Strings.ReportProblemContents });
        panel.Children.Add(_report);
        panel.Children.Add(_status);
        panel.Children.Add(buttons);
        Content = panel;

        SourceInitialized += (_, _) => ThemeManager.ApplyWindowTheme(this, NoBackdrop);
        Loaded += async (_, _) => await CollectAsync();
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
    }

    private async Task CollectAsync()
    {
        _status.Text = Strings.CollectingReport;
        try
        {
            _collected = await _collect(_lifetime.Token);
            _report.Text = _collected;
            _status.Text = string.Empty;
            _sendButton.IsEnabled = true;
            _description.Focus();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _status.Text = Strings.ReportCollectionFailed(exception.Message);
        }
    }

    private async Task SendAsync()
    {
        if (_collected is not { } report) return;

        _sendButton.IsEnabled = false;
        _description.IsReadOnly = true;
        _status.Text = Strings.SendingReport;
        try
        {
            await _send(_description.Text, report, _lifetime.Token);
            _status.Text = Strings.ReportSent;
            _sendButton.Visibility = Visibility.Collapsed;
            _closeButton.Content = Strings.Close;
            _closeButton.Margin = new Thickness(0);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SaveInstead(report, exception.Message);
        }
    }

    private void SaveInstead(string report, string reason)
    {
        _sendButton.Visibility = Visibility.Collapsed;
        _closeButton.Content = Strings.Close;
        _closeButton.Margin = new Thickness(0);
        string path;
        try
        {
            path = ProblemReportSender.SaveToFile(_description.Text, report);
        }
        catch (Exception exception)
        {
            _status.Text = Strings.ReportNotSaved($"{reason}; {exception.Message}");
            return;
        }

        _status.Text = Strings.ReportSaved(reason, path);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // The message already says where the file is.
        }
    }
}
