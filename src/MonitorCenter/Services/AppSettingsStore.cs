using System.IO;
using System.Text.Json;
using MonitorCenter.Models;

namespace MonitorCenter.Services;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan[] ReadRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(400)
    ];

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Set when the settings file exists but couldn't be read, so saving would replace settings that were never
    // loaded.
    private bool _savingBlocked;

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MonitorCenter",
            "settings.json");
    }

    internal string FilePath => _filePath;

    /// <summary>The settings as they were before the last save.</summary>
    internal string BackupPath => _filePath + ".bak";

    /// <summary>
    /// Loads the settings. A file that can't be parsed is kept under another name and the backup from the previous
    /// save is loaded instead. A file that can't be read, for example because another program has it locked, is
    /// retried; if it still can't be read, the backup is loaded and saving is turned off for this session so the
    /// file isn't overwritten. Saving is turned off as well when the backup is needed but can't be read, so the
    /// backup isn't overwritten either. A missing file with a backup beside it, as an unparseable file leaves
    /// behind, loads the backup.
    /// </summary>
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                if (await ReadWithRetriesAsync(_filePath, cancellationToken).ConfigureAwait(false) is { } settings)
                {
                    return settings;
                }
            }
            catch (JsonException)
            {
                if (!TryPreserveUnreadableFile())
                {
                    _savingBlocked = true;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _savingBlocked = true;
            }

            return await ReadBackupAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads a settings file, retrying when it can't be opened, for example because it is locked.</summary>
    /// <returns>The normalized settings, or null when the file doesn't exist.</returns>
    private static async Task<AppSettings?> ReadWithRetriesAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException && attempt < ReadRetryDelays.Length)
            {
            }

            await Task.Delay(ReadRetryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <returns>The normalized settings, or null when the file doesn't exist.</returns>
    private static async Task<AppSettings?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
        return Normalize(settings ?? new AppSettings());
    }

    private async Task<AppSettings> ReadBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ReadWithRetriesAsync(BackupPath, cancellationToken).ConfigureAwait(false) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _savingBlocked = true;
            return new AppSettings();
        }
    }

    /// <summary>Renames a settings file that can't be parsed, so the next save doesn't destroy it.</summary>
    private bool TryPreserveUnreadableFile()
    {
        try
        {
            var preservedPath = Path.Combine(
                Path.GetDirectoryName(_filePath)!,
                $"{Path.GetFileNameWithoutExtension(_filePath)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(_filePath)}");
            File.Move(_filePath, preservedPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_savingBlocked)
            {
                throw new IOException(
                    "MonitorCenter couldn't read its settings file, so it won't overwrite it. Restart MonitorCenter to save changes.");
            }

            var normalized = Normalize(settings);
            var content = JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions);
            if (await IsUnchangedAsync(content, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // The file being replaced was loaded or written by this app, so it becomes the backup that loading
            // falls back to.
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, BackupPath, overwrite: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> IsUnchangedAsync(byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return existing.AsSpan().SequenceEqual(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        settings.Profiles ??= [];
        settings.DisplayNames ??= [];
        settings.Profiles = settings.Profiles
            .Where(profile => profile is not null)
            .Take(MonitorCoordinator.MaxProfileCount)
            .ToList();

        var usedIds = new HashSet<Guid>();
        foreach (var profile in settings.Profiles)
        {
            if (profile.Id == Guid.Empty || !usedIds.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                usedIds.Add(profile.Id);
            }

            profile.Name = NormalizeName(profile.Name);
            profile.MonitorSettings = profile.MonitorSettings?.Where(monitor => monitor is not null).ToList() ?? [];
            foreach (var monitor in profile.MonitorSettings)
            {
                monitor.Identity ??= new MonitorIdentity();
                monitor.Brightness = monitor.Brightness is null
                    ? null
                    : Math.Clamp(monitor.Brightness.Value, 0, 100);
            }
        }

        var normalizedDisplayNames = new List<DisplayNamePreference>();
        var displayKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preference in settings.DisplayNames.Where(preference => preference is not null))
        {
            preference.Identity ??= new MonitorIdentity();
            preference.Name = NormalizeDisplayName(preference.Name);
            var key = !string.IsNullOrWhiteSpace(preference.Identity.Id)
                ? $"id:{preference.Identity.Id}"
                : !string.IsNullOrWhiteSpace(preference.Identity.Serial)
                    ? $"serial:{preference.Identity.Serial}"
                    : string.Empty;
            if (key.Length == 0 || preference.Name.Length == 0 || !displayKeys.Add(key))
            {
                continue;
            }

            normalizedDisplayNames.Add(preference);
        }
        settings.DisplayNames = normalizedDisplayNames;

        return settings;
    }

    internal static string NormalizeName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Profile";
        }

        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    internal static string NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }
}
