using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
[DoNotParallelize]
public sealed class FlyoutWindowTests
{
    [TestMethod]
    public void Flyout_LoadsAsTrayOnlyWindow_WithInlineProfileSaving()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            MonitorService? monitorService = null;
            MonitorCoordinator? coordinator = null;
            System.Windows.Application? application = null;
            try
            {
                var monitorApplication = new MonitorCenter.App
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                monitorApplication.InitializeComponent();
                application = monitorApplication;
                monitorService = new MonitorService();
                coordinator = new MonitorCoordinator(monitorService, Dispatcher.CurrentDispatcher);
                foreach (var name in new[] { "First", "Second", "Third" })
                {
                    coordinator.Profiles.Add(new BrightnessProfile { Name = name });
                }

                var viewModel = new FlyoutViewModel(coordinator);
                var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var refreshCalls = 0;
                var window = new FlyoutWindow(viewModel, _ => { refreshCalls++; return releaseRefresh.Task; });

                Assert.IsFalse(window.ShowInTaskbar);
                Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
                Assert.AreEqual(WindowStyle.None, window.WindowStyle);

                var trayProfiles = window.FindName("TrayProfilesList") as ItemsControl;
                Assert.IsNotNull(trayProfiles);
                Assert.AreEqual(MonitorCoordinator.MaxProfileCount, trayProfiles.Items.Count);
                Assert.IsNotNull(window.FindName("NewProfileButton") as Button);
                Assert.IsNotNull(window.FindName("NewProfilePanel") as Border);
                Assert.IsNotNull(window.FindName("ProfileNameTextBox") as TextBox);
                Assert.IsNull(window.FindName("RenameDisplayPanel"));
                Assert.IsNotNull(monitorApplication.TryFindResource("TileMenuButtonStyle") as Style);
                Assert.IsNotNull(monitorApplication.TryFindResource("InlineRenameTextBoxStyle") as Style);
                Assert.IsNotNull(monitorApplication.TryFindResource("DisplayMenuItemStyle") as Style);
                Assert.IsNull(monitorApplication.TryFindResource("StatusBannerStyle"));

                // Renaming swaps the name for an inline text box and the options button for a save button.
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.DataContext = viewModel;
                void Layout()
                {
                    root.Measure(new Size(354, double.PositiveInfinity));
                    root.Arrange(new Rect(root.DesiredSize));
                    root.UpdateLayout();
                }

                Layout();
                var tile = viewModel.ProfileTiles[1];
                var container = (FrameworkElement)trayProfiles.ItemContainerGenerator.ContainerFromItem(tile);
                var presenter = (ContentPresenter)container;
                var template = presenter.ContentTemplate;
                var textBox = (TextBox)template.FindName("ProfileRenameTextBox", presenter);
                var menuButton = (Button)template.FindName("ProfileMenuButton", presenter);
                var saveButton = (Button)template.FindName("ProfileSaveButton", presenter);
                Assert.AreEqual(Visibility.Collapsed, textBox.Visibility);
                Assert.AreEqual(Visibility.Visible, menuButton.Visibility);
                Assert.AreEqual(Visibility.Collapsed, saveButton.Visibility);

                viewModel.StartProfileRename(tile);
                Layout();
                Assert.AreEqual(Visibility.Visible, textBox.Visibility);
                Assert.AreEqual("Second", textBox.Text);
                Assert.IsTrue(textBox.ActualWidth > 0);
                Assert.AreEqual(Visibility.Collapsed, menuButton.Visibility);
                Assert.AreEqual(Visibility.Visible, saveButton.Visibility);

                textBox.Text = " ";
                Assert.IsFalse(saveButton.IsEnabled);

                viewModel.CancelRenames();
                Layout();
                Assert.AreEqual(Visibility.Collapsed, textBox.Visibility);
                Assert.AreEqual(Visibility.Visible, menuButton.Visibility);
                window.Content = root;

                // No real display discovery: hold a fake refresh open while shutdown cancels queued UI work.
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var activeRefresh = window.RefreshAsync(false);
                var pendingShow = window.ShowFlyoutAsync();
                var pendingToggle = window.ToggleAsync();
                var pendingRefresh = window.RefreshAsync(true);
                window.BeginShutdown();
                window.BeginShutdown();
                Finish(Task.WhenAll(pendingShow, pendingToggle, pendingRefresh));
                Assert.IsFalse(activeRefresh.IsCompleted, "Shutdown must let the active discovery drain.");
                Assert.IsFalse(window.IsVisible, "A queued activation reopened the flyout during shutdown.");
                Finish(Task.WhenAll(window.RefreshAsync(), window.ShowFlyoutAsync(), window.ToggleAsync()));
                releaseRefresh.SetResult();
                Finish(activeRefresh);
                Assert.AreEqual(1, refreshCalls, "Pending refreshes must not start after shutdown begins.");
                window.AllowCloseAndClose();

                void Finish(Task task)
                {
                    var frame = new DispatcherFrame();
                    _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                        TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                    task.GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (coordinator is not null)
                {
                    coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                if (monitorService is not null)
                {
                    monitorService.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                application?.Shutdown();
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(20)), "Flyout runtime test timed out.");
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
