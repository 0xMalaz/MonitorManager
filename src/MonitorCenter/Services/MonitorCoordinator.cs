using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MonitorCenter.Models;
using MonitorCenter.UI;

namespace MonitorCenter.Services;

internal sealed class MonitorCoordinator : INotifyPropertyChanged, IAsyncDisposable
{
    public const int MaxProfileCount = 3;

    private readonly MonitorService _monitorService;
    private readonly AppSettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _profileGate = new(1, 1);
    private readonly DisplayRecoveryScheduler _recovery;
    private readonly HashSet<string> _controllableIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRefreshing;
    private bool _disposed;
    private string _emptyMessage = Strings.NoDisplaysDetected;
    private Guid? _activeProfileId;
    private int _userAdjustments;
    private AppSettings _settings = new();

    public MonitorCoordinator(
        MonitorService monitorService,
        Dispatcher dispatcher,
        AppSettingsStore? settingsStore = null)
    {
        _monitorService = monitorService;
        _dispatcher = dispatcher;
        _settingsStore = settingsStore ?? new AppSettingsStore();
        _recovery = new DisplayRecoveryScheduler(RecheckUnavailableAsync);
        Monitors.CollectionChanged += OnMonitorsCollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MonitorsChanged;

    public ObservableCollection<MonitorRowViewModel> Monitors { get; } = [];
    public ObservableCollection<BrightnessProfile> Profiles { get; } = [];
    public IEnumerable<BrightnessProfile> TrayProfiles => Profiles.Take(MaxProfileCount);
    public bool HasTrayProfiles => Profiles.Count > 0;
    public bool CanCreateProfile => Profiles.Count < MaxProfileCount;
    public bool HasControllableMonitors => Monitors.Any(row => row.IsControllable);
    public bool ShowEmptyState => !IsRefreshing && Monitors.Count == 0;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value)
            {
                return;
            }

            _isRefreshing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set
        {
            if (string.Equals(_emptyMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _emptyMessage = value;
            OnPropertyChanged();
        }
    }

    public Guid? ActiveProfileId => _activeProfileId;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
            Profiles.Clear();
            foreach (var profile in _settings.Profiles.Take(MaxProfileCount))
            {
                Profiles.Add(profile);
            }
            NotifyProfileCollectionsChanged();
        });

        await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // This save only writes the loaded settings back normalized; changes the user makes later report
            // their own save failures.
        }
    }

    public async Task RefreshAsync(bool reprobeKnownDisplays = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RefreshCoreAsync(
                token => _monitorService.DiscoverAsync(reprobeKnownDisplays, token),
                isLastRecheck: false,
                showProgress: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Asks the displays that did not answer again; returns true when none needs recovering any more.</summary>
    private async Task<bool> RecheckUnavailableAsync(bool isLastRecheck, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return true;
        }

        return !await RefreshCoreAsync(
                _monitorService.RecheckUnavailableAsync,
                isLastRecheck,
                showProgress: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns>True when a display could not be controlled and rechecks are scheduled for it.</returns>
    private async Task<bool> RefreshCoreAsync(
        Func<CancellationToken, Task<IReadOnlyList<MonitorSnapshot>>> discover,
        bool isLastRecheck,
        bool showProgress,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (showProgress)
            {
                await _dispatcher.InvokeAsync(() => IsRefreshing = true);
            }

            IReadOnlyList<MonitorSnapshot> snapshots;
            try
            {
                snapshots = await discover(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await _dispatcher.InvokeAsync(() => EmptyMessage = Strings.DisplayDetectionFailed(exception.Message));
                return false;
            }

            var needsRecovery = !isLastRecheck && snapshots.Any(snapshot => !snapshot.IsControllable);
            if (needsRecovery && !_disposed)
            {
                _ = _recovery.StartAsync();
            }

            snapshots = DisplayRecoveryScheduler.MarkReconnecting(snapshots, _controllableIds, needsRecovery);
            _controllableIds.UnionWith(snapshots.Where(snapshot => snapshot.IsControllable).Select(snapshot => snapshot.Id));

            Dictionary<string, bool> previouslyControllable = [];
            List<MonitorRowViewModel> removedRows = [];
            await _dispatcher.InvokeAsync(() =>
            {
                previouslyControllable = Monitors.ToDictionary(
                    row => row.Snapshot.Id,
                    row => row.IsControllable,
                    StringComparer.OrdinalIgnoreCase);
                removedRows = UpdateMonitorRows(snapshots);

                EmptyMessage = Strings.NoDisplaysDetected;
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(HasControllableMonitors));
                MonitorsChanged?.Invoke(this, EventArgs.Empty);
            });

            foreach (var row in removedRows)
            {
                await row.DisposeAsync();
            }

            var reconnectedRows = await _dispatcher.InvokeAsync(() => Monitors
                .Where(row => row.IsControllable &&
                              (!previouslyControllable.TryGetValue(row.Snapshot.Id, out var wasControllable) ||
                               !wasControllable))
                .ToArray());
            await ApplyActiveProfileToRowsAsync(reconnectedRows, cancellationToken).ConfigureAwait(false);
            return needsRecovery;
        }
        finally
        {
            if (showProgress)
            {
                await _dispatcher.InvokeAsync(() => IsRefreshing = false);
            }

            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Reconciles <see cref="Monitors"/> with a discovery result in place, keeping the rows of displays that are
    /// still connected so the flyout is not rebuilt. Returns the rows that were removed. Must run on the dispatcher.
    /// </summary>
    private List<MonitorRowViewModel> UpdateMonitorRows(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        var existing = Monitors.ToDictionary(row => row.Snapshot.Id, StringComparer.OrdinalIgnoreCase);
        var removed = new List<MonitorRowViewModel>();
        var desired = new List<MonitorRowViewModel>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var displayName = MatchDisplayNamePreference(snapshot)?.Name ?? snapshot.DisplayName;
            var named = snapshot with { DisplayName = displayName };

            // A row only writes brightness when its display was controllable at creation, so a change in
            // controllability needs a new row.
            if (existing.Remove(snapshot.Id, out var row) && row.IsControllable == named.IsControllable)
            {
                row.UpdateSnapshot(named);
                desired.Add(row);
                continue;
            }

            if (row is not null)
            {
                removed.Add(row);
            }

            desired.Add(new MonitorRowViewModel(named, _monitorService, _dispatcher, WriteBrightnessAsync));
        }

        removed.AddRange(existing.Values);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < Monitors.Count && ReferenceEquals(Monitors[index], desired[index]))
            {
                continue;
            }

            var currentIndex = Monitors.IndexOf(desired[index]);
            if (currentIndex >= 0)
            {
                Monitors.Move(currentIndex, index);
            }
            else
            {
                Monitors.Insert(index, desired[index]);
            }
        }

        while (Monitors.Count > desired.Count)
        {
            Monitors.RemoveAt(Monitors.Count - 1);
        }

        return removed;
    }

    public async Task<BrightnessProfile> CreateProfileFromCurrentAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedName = AppSettingsStore.NormalizeName(name);
            var canCreate = await _dispatcher.InvokeAsync(() => Profiles.Count < MaxProfileCount);
            if (!canCreate)
            {
                throw new InvalidOperationException(Strings.TooManyProfiles);
            }

            var duplicateName = await _dispatcher.InvokeAsync(() => Profiles.Any(profile =>
                string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase)));
            if (duplicateName)
            {
                throw new InvalidOperationException(Strings.DuplicateProfileName);
            }

            var monitorSettings = await CaptureCurrentSetupAsync(cancellationToken).ConfigureAwait(false);
            var profile = new BrightnessProfile
            {
                Name = normalizedName,
                MonitorSettings = monitorSettings
            };

            await _dispatcher.InvokeAsync(() =>
            {
                Profiles.Add(profile);
                NotifyProfileCollectionsChanged();
            });
            await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _profileGate.Release();
        }
    }

    public async Task RenameProfileAsync(
        BrightnessProfile profile,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(Strings.EnterProfileName);
        }

        var normalizedName = AppSettingsStore.NormalizeName(name);
        await _profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var error = await _dispatcher.InvokeAsync(() =>
            {
                if (!Profiles.Contains(profile))
                {
                    return Strings.ProfileNoLongerExists;
                }

                if (Profiles.Any(other => !ReferenceEquals(other, profile) &&
                        string.Equals(other.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                {
                    return Strings.DuplicateProfileName;
                }

                profile.Name = normalizedName;
                NotifyProfileCollectionsChanged();
                return null;
            });
            if (error is not null)
            {
                throw new InvalidOperationException(error);
            }

            await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _profileGate.Release();
        }
    }

    public async Task DeleteProfileAsync(
        BrightnessProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var removed = await _dispatcher.InvokeAsync(() =>
            {
                if (!Profiles.Remove(profile))
                {
                    return false;
                }

                if (_activeProfileId == profile.Id)
                {
                    _activeProfileId = null;
                    OnPropertyChanged(nameof(ActiveProfileId));
                }

                NotifyProfileCollectionsChanged();
                return true;
            });
            if (removed)
            {
                await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _profileGate.Release();
        }
    }

    public async Task RenameDisplayAsync(
        MonitorRowViewModel row,
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = AppSettingsStore.NormalizeDisplayName(name);
        if (normalizedName.Length == 0)
        {
            throw new InvalidOperationException(Strings.EnterDisplayName);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            var preference = MatchDisplayNamePreference(row.Snapshot);
            if (preference is null)
            {
                preference = new DisplayNamePreference
                {
                    Identity = CreateIdentity(row.Snapshot)
                };
                _settings.DisplayNames.Add(preference);
            }

            preference.Name = normalizedName;
            row.SetDisplayName(normalizedName);
        });
        await SaveSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets every connected display in the profile to its saved brightness. Profiles are applied one at a time, and
    /// each display's value goes through its tile's write queue, so a quick second click or a tile write still in
    /// flight can't leave displays from two different setups.
    /// </summary>
    /// <returns>True when every display in the profile that is connected was set.</returns>
    public async Task<bool> ApplyProfileAsync(
        BrightnessProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return false;
        }

        await _profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read on the dispatcher, where MarkCustom counts the adjustments.
            var (rows, adjustmentsBefore) = await _dispatcher.InvokeAsync(() => (Monitors.ToArray(), _userAdjustments));
            var hasFailures = false;
            var matched = 0;
            foreach (var setting in profile.MonitorSettings)
            {
                var row = MatchMonitor(rows, setting.Identity);
                if (row is null || setting.Brightness is not int brightness)
                {
                    continue;
                }

                matched++;
                try
                {
                    // Null means newer input on the tile replaced the profile's value before it was written.
                    if (await row.SetBrightnessAsync(brightness, cancellationToken).ConfigureAwait(false) is null)
                    {
                        hasFailures = true;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    hasFailures = true;
                }
            }

            // A display the user adjusted while the profile was applied means the setup is no longer the profile.
            // This is checked on the dispatcher, in the same step that marks the profile active, so an adjustment
            // can't land in between.
            return await _dispatcher.InvokeAsync(() =>
            {
                var applied = matched > 0 && !hasFailures && _userAdjustments == adjustmentsBefore;
                _activeProfileId = applied ? profile.Id : null;
                OnPropertyChanged(nameof(ActiveProfileId));
                return applied;
            });
        }
        finally
        {
            _profileGate.Release();
        }
    }

    private async Task<List<ProfileMonitorSetting>> CaptureCurrentSetupAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _dispatcher.InvokeAsync(() => Monitors.Where(row => row.IsControllable).ToArray());
        if (rows.Length == 0)
        {
            throw new InvalidOperationException(Strings.NoControllableDisplays);
        }

        foreach (var row in rows)
        {
            await row.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _dispatcher.InvokeAsync(() => rows.Select(row => new ProfileMonitorSetting
        {
            Identity = new MonitorIdentity
            {
                Id = row.Snapshot.Id,
                Serial = row.Snapshot.Serial,
                FriendlyName = row.Snapshot.FriendlyName
            },
            Brightness = row.AppliedPercent
        }).ToList());
    }

    /// <summary>Sets displays that just became controllable to the active profile's brightness, if one is active.</summary>
    internal async Task ApplyActiveProfileToRowsAsync(
        IReadOnlyList<MonitorRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        // Taken so a profile the user applies meanwhile can't interleave with this one. The active profile is read
        // only after it is taken, so this follows a profile that was still being applied when the displays
        // reconnected.
        await _profileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await _dispatcher.InvokeAsync(() =>
                Profiles.FirstOrDefault(item => item.Id == _activeProfileId));
            if (profile is not null)
            {
                await ApplyProfileToRowsAsync(profile, rows, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _profileGate.Release();
        }
    }

    private static async Task ApplyProfileToRowsAsync(
        BrightnessProfile profile,
        IReadOnlyList<MonitorRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            var setting = MatchProfileSetting(profile, row.Snapshot);
            if (setting?.Brightness is not int brightness)
            {
                continue;
            }

            try
            {
                await row.SetBrightnessAsync(brightness, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // The display tile already communicates its support state.
            }
        }
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        _settings.Profiles = await _dispatcher.InvokeAsync(() => Profiles.Take(MaxProfileCount).ToList());
        await _settingsStore.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> WriteBrightnessAsync(
        MonitorRowViewModel source,
        int requested,
        BrightnessWriteMode mode,
        CancellationToken cancellationToken)
    {
        var result = await _monitorService.SetBrightnessAsync(source.Snapshot.Id, requested, mode, cancellationToken)
            .ConfigureAwait(false);
        return result.ActualPercent;
    }

    private void OnMonitorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        foreach (MonitorRowViewModel row in e.OldItems ?? Array.Empty<MonitorRowViewModel>())
        {
            row.UserAdjusted -= OnRowUserAdjusted;
        }

        foreach (MonitorRowViewModel row in e.NewItems ?? Array.Empty<MonitorRowViewModel>())
        {
            row.UserAdjusted += OnRowUserAdjusted;
        }
    }

    private void OnRowUserAdjusted(object? sender, EventArgs e) => MarkCustom();

    /// <summary>Called on the dispatcher when the user changes a display by hand.</summary>
    private void MarkCustom()
    {
        _userAdjustments++;
        _activeProfileId = null;
        OnPropertyChanged(nameof(ActiveProfileId));
    }

    private static MonitorRowViewModel? MatchMonitor(
        IEnumerable<MonitorRowViewModel> rows,
        MonitorIdentity identity)
    {
        var rowArray = rows as MonitorRowViewModel[] ?? rows.ToArray();
        var exact = rowArray.FirstOrDefault(row =>
            string.Equals(row.Snapshot.Id, identity.Id, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || string.IsNullOrWhiteSpace(identity.Serial))
        {
            return exact;
        }

        var serialMatches = rowArray.Where(row =>
            string.Equals(row.Snapshot.Serial, identity.Serial, StringComparison.OrdinalIgnoreCase)).ToArray();
        return serialMatches.Length == 1 ? serialMatches[0] : null;
    }

    private static ProfileMonitorSetting? MatchProfileSetting(
        BrightnessProfile profile,
        MonitorSnapshot snapshot)
    {
        var exact = profile.MonitorSettings.FirstOrDefault(setting =>
            string.Equals(setting.Identity.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || string.IsNullOrWhiteSpace(snapshot.Serial))
        {
            return exact;
        }

        var serialMatches = profile.MonitorSettings.Where(setting =>
            string.Equals(setting.Identity.Serial, snapshot.Serial, StringComparison.OrdinalIgnoreCase)).ToArray();
        return serialMatches.Length == 1 ? serialMatches[0] : null;
    }

    private DisplayNamePreference? MatchDisplayNamePreference(MonitorSnapshot snapshot)
    {
        var exact = _settings.DisplayNames.FirstOrDefault(preference =>
            string.Equals(preference.Identity.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase));
        if (exact is not null || string.IsNullOrWhiteSpace(snapshot.Serial))
        {
            return exact;
        }

        var serialMatches = _settings.DisplayNames.Where(preference =>
            string.Equals(preference.Identity.Serial, snapshot.Serial, StringComparison.OrdinalIgnoreCase)).ToArray();
        return serialMatches.Length == 1 ? serialMatches[0] : null;
    }

    private static MonitorIdentity CreateIdentity(MonitorSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        Serial = snapshot.Serial,
        FriendlyName = snapshot.FriendlyName
    };

    private void NotifyProfileCollectionsChanged()
    {
        OnPropertyChanged(nameof(TrayProfiles));
        OnPropertyChanged(nameof(HasTrayProfiles));
        OnPropertyChanged(nameof(CanCreateProfile));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recovery.Dispose();

        // Let a refresh, recheck or profile operation that is already running finish before its gate and the rows
        // it writes to go away.
        await _refreshGate.WaitAsync();
        await _profileGate.WaitAsync();
        foreach (var row in Monitors.ToArray())
        {
            await row.DisposeAsync();
        }
        Monitors.Clear();
        _refreshGate.Dispose();
        _profileGate.Dispose();
    }
}
