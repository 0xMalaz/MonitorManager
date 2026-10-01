using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class ApplicationBehaviorTests
{
    [TestMethod]
    public void StartupCommand_QuotesExecutableAndAddsSilentArgument()
    {
        var path = @"C:\Users\Example User\Apps\MonitorCenter.exe";

        Assert.AreEqual(
            $"\"{Path.GetFullPath(path)}\" --startup",
            StartupRegistration.BuildCommand(path));
    }

    [TestMethod]
    public void CommandLine_RecognizesStartupCaseInsensitively()
    {
        Assert.IsTrue(CommandLineOptions.Parse(new[] { "--STARTUP" }).IsStartupLaunch);
        Assert.IsFalse(CommandLineOptions.Parse(Array.Empty<string>()).IsStartupLaunch);
    }

    [TestMethod]
    public void SingleInstanceNames_AreUserScopedAndSafe()
    {
        var names = SingleInstanceCoordinator.CreateNames("S-1-5-21\\Example User");

        StringAssert.StartsWith(names.MutexName, @"Local\MonitorCenter.");
        StringAssert.StartsWith(names.ActivationEventName, @"Local\MonitorCenter.Activate.");
        Assert.IsFalse(names.MutexName.Contains(' '));
        Assert.IsFalse(names.MutexName[@"Local\MonitorCenter.".Length..].Contains('\\'));
    }

    [TestMethod]
    public void ApplicationIcon_IsEmbeddedAndDecodesAtTraySize()
    {
        using var stream = typeof(MonitorCenter.App).Assembly.GetManifestResourceStream(
            "MonitorCenter.Assets.MonitorCenter.ico");
        Assert.IsNotNull(stream);
        Assert.IsTrue(stream.Length > 1_000, "The icon should contain multiple raster sizes.");

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.AreEqual((ushort)0, reader.ReadUInt16());
        Assert.AreEqual((ushort)1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.AreEqual(7, count);

        var entries = new List<(int Width, int Height, uint Size, uint Offset)>();
        for (var index = 0; index < count; index++)
        {
            var widthByte = reader.ReadByte();
            var heightByte = reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            Assert.AreEqual((ushort)1, reader.ReadUInt16());
            Assert.AreEqual((ushort)32, reader.ReadUInt16());
            entries.Add((
                widthByte == 0 ? 256 : widthByte,
                heightByte == 0 ? 256 : heightByte,
                reader.ReadUInt32(),
                reader.ReadUInt32()));
        }

        CollectionAssert.AreEqual(
            new[] { 16, 20, 24, 32, 48, 64, 256 },
            entries.Select(entry => entry.Width).ToArray());
        foreach (var entry in entries)
        {
            Assert.AreEqual(entry.Width, entry.Height);
            Assert.IsTrue(entry.Offset + entry.Size <= stream.Length);
            stream.Position = entry.Offset;
            CollectionAssert.AreEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                reader.ReadBytes(8),
                $"The {entry.Width}px icon frame should be a complete PNG image.");
        }

        stream.Position = 0;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        Assert.AreEqual(32, icon.Width);
        Assert.AreEqual(32, icon.Height);
    }
}
