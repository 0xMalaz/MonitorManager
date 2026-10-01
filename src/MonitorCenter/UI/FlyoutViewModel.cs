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
    private MonitorRowViewModel? _renameMonitor;
    private bool _isRenameEditorOpen;
    private string _renameDisplayName = string.Empty;
    private string? _renameEditorMessage;

    public FlyoutViewModel(MonitorCoordinator coordinator)
    {
        _coordinator = coordinator;
        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        _coordinator.MonitorsChanged += (_, _) =>
        {
            if (IsRenameEditorOpen)
            {
                CloseRenameEditor();
            }

            OnPropertyChanged(nameof(Monitors));
            OnPropertyChanged(nameof(CanSaveCurrentSetup));
            OnPropertyChanged(nameof(CanSaveNewProfile));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MonitorRowViewModel> Monitors => _coordinator.Monitors;
    public IEnumerable<BrightnessProfile> TrayProfiles => _coordinator.TrayProfiles;
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

    public bool IsRenameEditorOpen
    {
        get => _isRenameEditorOpen;
        private set
        {
            if (_isRenameEditorOpen == value)
            {
                return;
            }

            _isRenameEditorOpen = value;
            OnPropertyChanged();
        }
    }

    public string RenameEditorTitle => _renameMonitor is null
        ? "Rename display"
        : $"Rename {_renameMonitor.DisplayName}";

    public string RenameDisplayName
    {
        get => _renameDisplayName;
        set
        {
            if (string.Equals(_renameDisplayName, value, StringComparison.Ordinal))
            {
                return;
            }

            _renameDisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveDisplayName));
        }
    }

    public string? RenameEditorMessage
    {
        get => _renameEditorMessage;
        private set
        {
            if (string.Equals(_renameEditorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _renameEditorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRenameEditorMessage));
        }
    }

    public bool HasRenameEditorMessage => !string.IsNullOrWhiteSpace(RenameEditorMessage);
    public bool CanSaveDisplayName =>
        _renameMonitor is not null && !string.IsNullOrWhiteSpace(RenameDisplayName);

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _coordinator.RefreshAsync(cancellationToken);

    public Task ApplyProfileAsync(BrightnessProfile profile, CancellationToken cancellationToken = default) =>
        _coordinator.ApplyProfileAsync(profile, cancellationToken);

    public void OpenProfileEditor()
    {
        CloseRenameEditor();
        ProfileEditorMessage = null;
        NewProfileName = GetSuggestedProfileName();
        IsProfileEditorOpen = true;
    }

    public void CloseProfileEditor()
    {
        ProfileEditorMessage = null;
        IsProfileEditorOpen = false;
    }

    public void OpenRenameEditor(MonitorRowViewModel row)
    {
        CloseProfileEditor();
        _renameMonitor = row;
        RenameEditorMessage = null;
        RenameDisplayName = row.DisplayName;
        OnPropertyChanged(nameof(RenameEditorTitle));
        OnPropertyChanged(nameof(CanSaveDisplayName));
        IsRenameEditorOpen = true;
    }

    public void CloseRenameEditor()
    {
        RenameEditorMessage = null;
        IsRenameEditorOpen = false;
        _renameMonitor = null;
        OnPropertyChanged(nameof(RenameEditorTitle));
        OnPropertyChanged(nameof(CanSaveDisplayName));
    }

    public async Task SaveDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        if (_renameMonitor is null)
        {
            return;
        }

        try
        {
            RenameEditorMessage = null;
            await _coordinator.RenameDisplayAsync(_renameMonitor, RenameDisplayName, cancellationToken);
            CloseRenameEditor();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RenameEditorMessage = exception.Message;
        }
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

        if (property is nameof(CanCreateProfile) or nameof(CanSaveCurrentSetup))
        {
            OnPropertyChanged(nameof(CanSaveNewProfile));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
