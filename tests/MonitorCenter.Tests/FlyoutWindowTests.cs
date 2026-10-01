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

                var window = new FlyoutWindow(new FlyoutViewModel(coordinator));

                Assert.IsFalse(window.ShowInTaskbar);
                Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
                Assert.AreEqual(WindowStyle.None, window.WindowStyle);

                var trayProfiles = window.FindName("TrayProfilesList") as ItemsControl;
                Assert.IsNotNull(trayProfiles);
                Assert.AreEqual(MonitorCoordinator.MaxProfileCount, trayProfiles.Items.Count);
                Assert.IsNotNull(window.FindName("NewProfileButton") as Button);
                Assert.IsNotNull(window.FindName("NewProfilePanel") as Border);
                Assert.IsNotNull(window.FindName("ProfileNameTextBox") as TextBox);
                Assert.IsNotNull(window.FindName("RenameDisplayPanel") as Border);
                Assert.IsNotNull(window.FindName("RenameDisplayTextBox") as TextBox);
                Assert.IsNotNull(monitorApplication.TryFindResource("DisplayMenuItemStyle") as Style);
                Assert.IsNull(monitorApplication.TryFindResource("StatusBannerStyle"));

                window.AllowCloseAndClose();
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
