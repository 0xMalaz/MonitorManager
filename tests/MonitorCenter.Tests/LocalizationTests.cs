using System.Globalization;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Localization;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class LocalizationTests
{
    /// <summary>Other tests compare against English text, so they run in English whatever the machine's language is.</summary>
    [AssemblyInitialize]
    public static void UseEnglish(TestContext _)
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }

    [TestMethod]
    public void Text_FollowsTheWindowsDisplayLanguage()
    {
        Assert.AreEqual("Check for updates", InCulture("en-US", () => Strings.CheckForUpdates));
        Assert.AreEqual("Güncellemeleri denetle", InCulture("tr-TR", () => Strings.CheckForUpdates));
        Assert.AreEqual("Check for updates", InCulture("de-DE", () => Strings.CheckForUpdates));
    }

    [TestMethod]
    public void Percentages_UseTheTurkishSignOrder()
    {
        Assert.AreEqual("50%", InCulture("en-US", () => Strings.Percent(50)));
        Assert.AreEqual("%50", InCulture("tr-TR", () => Strings.Percent(50)));
        Assert.AreEqual("1.2.0 indiriliyor: %40", InCulture("tr-TR", () => Strings.DownloadProgress("1.2.0", 40)));
    }

    [TestMethod]
    public void TechnicalTerms_StayUntranslated()
    {
        StringAssert.Contains(InCulture("tr-TR", () => Strings.BrightnessUnavailable), "DDC/CI");
        StringAssert.Contains(InCulture("tr-TR", () => Strings.UpdatesUnavailable), "MonitorCenter-Setup.exe");
    }

    [TestMethod]
    public void EveryText_HasDistinctTurkishAndEnglishVersions()
    {
        var properties = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string));

        foreach (var property in properties)
        {
            var english = InCulture("en-US", () => (string)property.GetValue(null)!);
            var turkish = InCulture("tr-TR", () => (string)property.GetValue(null)!);
            Assert.IsFalse(string.IsNullOrWhiteSpace(turkish), property.Name);
            Assert.AreNotEqual(english, turkish, $"{property.Name} has no Turkish text.");
        }
    }

    private static T InCulture<T>(string name, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
