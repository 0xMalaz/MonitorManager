using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Localization;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReportProblemWindowTests
{
    [TestMethod]
    [DataRow("en-US")]
    [DataRow("tr-TR")]
    public void Buttons_StayVisibleInASmallWindowWithALongMessage(string culture)
    {
        RunOnSta(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var window = new ReportProblemWindow(
                _ => Task.FromResult(string.Empty),
                (_, _, _) => Task.CompletedTask)
            {
                Resources = FlyoutWindowTests.LoadApplicationResources()
            };
            window.Status.Text = Strings.ReportSaved(
                "The report service answered 502.",
                @"C:\Users\Someone\AppData\Local\MonitorCenter\Reports\MonitorCenter-report-20261018-120000.txt");
            var layout = (FrameworkElement)window.Content;

            foreach (var size in new[] { new Size(640, 330), new Size(420, 400), new Size(640, 680) })
            {
                layout.Measure(size);
                layout.Arrange(new Rect(size));
                layout.UpdateLayout();

                var button = window.SendButton;
                var bottomRight = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), layout);
                Assert.IsTrue(button.ActualHeight > 0, $"The Send button has no height at {size}.");
                Assert.IsTrue(bottomRight.Y <= size.Height + 0.5, $"The Send button ends at {bottomRight.Y} in a {size} window.");
                Assert.IsTrue(bottomRight.X <= size.Width + 0.5, $"The Send button ends at x={bottomRight.X} in a {size} window.");
            }

            window.Close();
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "The window test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
