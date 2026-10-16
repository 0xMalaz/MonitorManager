using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.UI;

internal sealed class FlyoutViewModel : INotifyPropertyChanged
{
    private readonly MonitorCoordinator _coordinator;
    private bool _isProfileEditorOpen;
    private string _newProfileName = "Profile 1";
    private string? _profileEditorMessage;
    private string? _profileRenameMessage;
    private string? _displayRenameMessage;

    public FlyoutViewModel(MonitorCoordinator coordinator)
    {
        _coordinator = coordinator;
        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        _coordinator.MonitorsChanged += (_, _) =>
        {
            if (DisplayRenameMessage is not null && !Monitors.Any(row => row.IsRenaming))
            {
                DisplayRenameMessage = null;
            }

            OnPropertyChanged(nameof(Monitors));
            OnPropertyChanged(nameof(CanSaveCurrentSetup));
            OnPropertyChanged(nameof(CanSaveNewProfile));
        };
        SyncProfileTiles();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MonitorRowViewModel> Monitors => _coordinator.Monitors;
    public IEnumerable<BrightnessProfile> TrayProfiles => _coordinator.TrayProfiles;
    public ObservableCollection<ProfileTileViewModel> ProfileTiles { get; } = [];
    public bool HasTrayProfiles => _coordinator.HasTrayProfiles;
    public bool CanCreateProfile => _coordinator.CanCreateProfile;
    public bool CanSaveCurrentSetup => _coordinator.HasControllableMonitors;
    public bool IsRefreshing => _coordinator.IsRefreshing;
    public bool ShowEmptyState => _coordinator.ShowEmptyState;
    public string EmptyMessage => _coordinator.EmptyMessage;

    public bool IsProfileEditorOpen
    {
        get => _isProfileEditorOpen;
        private set
        {
            if (_isProfileEditorOpen == value)
            {
                return;
            }

            _isProfileEditorOpen = value;
            OnPropertyChanged();
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (string.Equals(_newProfileName, value, StringComparison.Ordinal))
            {
                return;
            }

            _newProfileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveNewProfile));
        }
    }

    public string? ProfileEditorMessage
    {
        get => _profileEditorMessage;
        private set
        {
            if (string.Equals(_profileEditorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _profileEditorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProfileEditorMessage));
        }
    }

    public bool HasProfileEditorMessage => !string.IsNullOrWhiteSpace(ProfileEditorMessage);
    public bool CanSaveNewProfile =>
        CanCreateProfile && CanSaveCurrentSetup && !string.IsNullOrWhiteSpace(NewProfileName);

    public string? ProfileRenameMessage
    {
        get => _profileRenameMessage;
        private set
        {
            if (string.Equals(_profileRenameMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _profileRenameMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProfileRenameMessage));
        }
    }

    public bool HasProfileRenameMessage => !string.IsNullOrWhiteSpace(ProfileRenameMessage);

    public string? DisplayRenameMessage
    {
        get => _displayRenameMessage;
        private set
        {
            if (string.Equals(_displayRenameMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayRenameMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDisplayRenameMessage));
        }
    }

    public bool HasDisplayRenameMessage => !string.IsNullOrWhiteSpace(DisplayRenameMessage);

    public bool IsRenaming =>
        ProfileTiles.Any(tile => tile.IsRenaming) || Monitors.Any(row => row.IsRenaming);

    public Task RefreshAsync(bool reprobeKnownDisplays = true, CancellationToken cancellationToken = default) =>
        _coordinator.RefreshAsync(reprobeKnownDisplays, cancellationToken);

    public Task ApplyProfileAsync(BrightnessProfile profile, CancellationToken cancellationToken = default) =>
        _coordinator.ApplyProfileAsync(profile, cancellationToken);

    public void OpenProfileEditor()
    {
        CancelRenames();
        ProfileEditorMessage = null;
        NewProfileName = GetSuggestedProfileName();
        IsProfileEditorOpen = true;
    }

    public void CloseProfileEditor()
    {
        ProfileEditorMessage = null;
        IsProfileEditorOpen = false;
    }

    public void StartProfileRename(ProfileTileViewModel tile)
    {
        CancelRenames();
        CloseProfileEditor();
        tile.EditName = tile.Name;
        tile.IsRenaming = true;
    }

    public async Task SaveProfileNameAsync(ProfileTileViewModel tile, CancellationToken cancellationToken = default)
    {
        if (!tile.IsRenaming || !tile.CanSaveName)
        {
            return;
        }

        try
        {
            ProfileRenameMessage = null;
            await _coordinator.RenameProfileAsync(tile.Profile, tile.EditName, cancellationToken);
            tile.IsRenaming = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ProfileRenameMessage = exception.Message;
        }
    }

    public async Task DeleteProfileAsync(ProfileTileViewModel tile, CancellationToken cancellationToken = default)
    {
        CancelRenames();
        try
        {
            await _coordinator.DeleteProfileAsync(tile.Profile, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ProfileRenameMessage = exception.Message;
        }
    }

    public void StartDisplayRename(MonitorRowViewModel row)
    {
        CancelRenames();
        CloseProfileEditor();
        row.EditName = row.DisplayName;
        row.IsRenaming = true;
    }

    public async Task SaveDisplayNameAsync(MonitorRowViewModel row, CancellationToken cancellationToken = default)
    {
        if (!row.IsRenaming || !row.CanSaveName)
        {
            return;
        }

        try
        {
            DisplayRenameMessage = null;
            await _coordinator.RenameDisplayAsync(row, row.EditName, cancellationToken);
            row.IsRenaming = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DisplayRenameMessage = exception.Message;
        }
    }

    public void CancelRenames()
    {
        foreach (var tile in ProfileTiles)
        {
            tile.IsRenaming = false;
        }

        foreach (var row in Monitors)
        {
            row.IsRenaming = false;
        }

        ProfileRenameMessage = null;
        DisplayRenameMessage = null;
    }

    public async Task CreateProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ProfileEditorMessage = null;
            await _coordinator.CreateProfileFromCurrentAsync(NewProfileName, cancellationToken);
            IsProfileEditorOpen = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ProfileEditorMessage = exception.Message;
        }
    }

    private string GetSuggestedProfileName()
    {
        for (var index = 1; index <= MonitorCoordinator.MaxProfileCount; index++)
        {
            var candidate = $"Profile {index}";
            if (!TrayProfiles.Any(profile =>
                    string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return "Profile";
    }

    /// <summary>Keeps one tile per tray profile, reusing tiles so a rename in progress survives the update.</summary>
    private void SyncProfileTiles()
    {
        var existing = ProfileTiles.ToDictionary(tile => tile.Profile);
        var desired = TrayProfiles
            .Select(profile => existing.TryGetValue(profile, out var tile) ? tile : new ProfileTileViewModel(profile))
            .ToList();

        // Keep WPF item containers (and their focus/edit state) alive when only a name changes.
        for (var index = ProfileTiles.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(ProfileTiles[index]))
            {
                ProfileTiles.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var tile = desired[index];
            tile.RefreshName();
            if (index < ProfileTiles.Count && ReferenceEquals(ProfileTiles[index], tile))
            {
                continue;
            }

            var currentIndex = ProfileTiles.IndexOf(tile);
            if (currentIndex >= 0)
            {
                ProfileTiles.Move(currentIndex, index);
            }
            else
            {
                ProfileTiles.Insert(index, tile);
            }
        }

        if (!ProfileTiles.Any(tile => tile.IsRenaming))
        {
            ProfileRenameMessage = null;
        }
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var property = e.PropertyName switch
        {
            nameof(MonitorCoordinator.TrayProfiles) => nameof(TrayProfiles),
            nameof(MonitorCoordinator.HasTrayProfiles) => nameof(HasTrayProfiles),
            nameof(MonitorCoordinator.CanCreateProfile) => nameof(CanCreateProfile),
            nameof(MonitorCoordinator.HasControllableMonitors) => nameof(CanSaveCurrentSetup),
            nameof(MonitorCoordinator.IsRefreshing) => nameof(IsRefreshing),
            nameof(MonitorCoordinator.ShowEmptyState) => nameof(ShowEmptyState),
            nameof(MonitorCoordinator.EmptyMessage) => nameof(EmptyMessage),
            _ => e.PropertyName
        };
        OnPropertyChanged(property);

        if (property is nameof(TrayProfiles))
        {
            SyncProfileTiles();
        }

        if (property is nameof(CanCreateProfile) or nameof(CanSaveCurrentSetup))
        {
            OnPropertyChanged(nameof(CanSaveNewProfile));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
