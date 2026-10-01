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

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MonitorCenter",
            "settings.json");
    }

    internal string FilePath => _filePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_filePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return Normalize(settings ?? new AppSettings());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = Normalize(settings);
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
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        settings.Profiles ??= [];
        settings.DisplayNames ??= [];
        settings.Profiles = settings.Profiles.Take(MonitorCoordinator.MaxProfileCount).ToList();

        var usedIds = new HashSet<Guid>();
        foreach (var profile in settings.Profiles)
        {
            if (profile.Id == Guid.Empty || !usedIds.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                usedIds.Add(profile.Id);
            }

            profile.Name = NormalizeName(profile.Name);
            profile.MonitorSettings ??= [];
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
        foreach (var preference in settings.DisplayNames)
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
