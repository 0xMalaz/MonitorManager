using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class AppSettingsStoreTests
{
    [TestMethod]
    public async Task SaveAndLoad_NormalizesProfilesAndEnforcesMaximum()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MonitorCenter.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "settings.json");
        try
        {
            var store = new AppSettingsStore(file);
            var settings = new AppSettings
            {
                Profiles =
                [
                    CreateProfile("  Morning  ", 120),
                    CreateProfile("Work", 60),
                    CreateProfile("Evening", -10),
                    CreateProfile("Ignored", 50)
                ],
                DisplayNames =
                [
                    new DisplayNamePreference
                    {
                        Identity = new MonitorIdentity { Id = "monitor-1", Serial = "ABC" },
                        Name = "  Main Display  "
                    },
                    new DisplayNamePreference
                    {
                        Identity = new MonitorIdentity { Id = "MONITOR-1", Serial = "ABC" },
                        Name = "Ignored duplicate"
                    },
                    new DisplayNamePreference
                    {
                        Identity = new MonitorIdentity { Serial = "XYZ" },
                        Name = "Side Display"
                    },
                    new DisplayNamePreference
                    {
                        Identity = new MonitorIdentity(),
                        Name = "Missing identity"
                    }
                ]
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(MonitorCoordinator.MaxProfileCount, loaded.Profiles.Count);
            CollectionAssert.AreEqual(
                new[] { "Morning", "Work", "Evening" },
                loaded.Profiles.Select(profile => profile.Name).ToArray());
            CollectionAssert.AreEqual(
                new int?[] { 100, 60, 0 },
                loaded.Profiles.Select(profile => profile.MonitorSettings.Single().Brightness).ToArray());
            CollectionAssert.AreEqual(
                new[] { "Main Display", "Side Display" },
                loaded.DisplayNames.Select(preference => preference.Name).ToArray());
            Assert.IsFalse(File.Exists(file + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_LegacySettingsKeepsBrightnessProfilesAndDropsAppUiFeaturesOnSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MonitorCenter.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        var profileId = Guid.NewGuid();
        try
        {
            var legacyJson = $$"""
                {
                  "SchemaVersion": 2,
                  "Monitors": [{ "CustomName": "Work display", "PinnedControls": ["Volume"] }],
                  "Profiles": [
                    {
                      "Id": "{{profileId}}",
                      "Name": "Work",
                      "MonitorSettings": [
                        {
                          "Identity": { "Id": "monitor-1", "Serial": "ABC", "FriendlyName": "Display" },
                          "Brightness": 64,
                          "Contrast": 70,
                          "InputSource": 15
                        }
                      ]
                    }
                  ],
                  "Automation": { "Enabled": true, "Rules": [] },
                  "Shortcuts": [{ "Gesture": "Ctrl+Alt+Up" }],
                  "TrayScroll": { "Enabled": true, "Step": 4 }
                }
                """;
            await File.WriteAllTextAsync(file, legacyJson);

            var store = new AppSettingsStore(file);
            var loaded = await store.LoadAsync();

            Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(profileId, loaded.Profiles.Single().Id);
            Assert.AreEqual(64, loaded.Profiles.Single().MonitorSettings.Single().Brightness);

            await store.SaveAsync(loaded);
            var rewrittenJson = await File.ReadAllTextAsync(file);
            Assert.IsFalse(rewrittenJson.Contains("Monitors", StringComparison.Ordinal));
            Assert.IsFalse(rewrittenJson.Contains("Contrast", StringComparison.Ordinal));
            Assert.IsFalse(rewrittenJson.Contains("InputSource", StringComparison.Ordinal));
            Assert.IsFalse(rewrittenJson.Contains("Automation", StringComparison.Ordinal));
            Assert.IsFalse(rewrittenJson.Contains("Shortcuts", StringComparison.Ordinal));
            Assert.IsFalse(rewrittenJson.Contains("TrayScroll", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_CorruptJsonReturnsDefaults()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "{not-json");
            var loaded = await new AppSettingsStore(file).LoadAsync();
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(0, loaded.Profiles.Count);
            Assert.AreEqual(0, loaded.DisplayNames.Count);
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public async Task Save_UnchangedSettingsDoesNotRewriteFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MonitorCenter.Tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "settings.json");
        try
        {
            var store = new AppSettingsStore(file);
            var settings = new AppSettings { Profiles = [CreateProfile("Work", 60)] };
            await store.SaveAsync(settings);

            var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, marker);
            await store.SaveAsync(await store.LoadAsync());
            Assert.AreEqual(marker, File.GetLastWriteTimeUtc(file));

            settings.Profiles[0].Name = "Home";
            await store.SaveAsync(settings);
            Assert.AreNotEqual(marker, File.GetLastWriteTimeUtc(file));
            Assert.AreEqual("Home", (await store.LoadAsync()).Profiles.Single().Name);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static BrightnessProfile CreateProfile(string name, int brightness) => new()
    {
        Name = name,
        MonitorSettings =
        [
            new ProfileMonitorSetting
            {
                Identity = new MonitorIdentity { Id = name },
                Brightness = brightness
            }
        ]
    };
}
