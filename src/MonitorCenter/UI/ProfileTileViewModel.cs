using System.ComponentModel;
using System.Runtime.CompilerServices;
using MonitorCenter.Models;

namespace MonitorCenter.UI;

/// <summary>A profile button in the flyout, with the state of its inline rename.</summary>
internal sealed class ProfileTileViewModel : INotifyPropertyChanged
{
    private bool _isRenaming;
    private bool _isActive;
    private string _editName = string.Empty;

    public ProfileTileViewModel(BrightnessProfile profile)
    {
        Profile = profile;
        Levels = profile.MonitorSettings.Select(setting => setting.Brightness).ToArray();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BrightnessProfile Profile { get; }
    public string Name => Profile.Name;

    /// <summary>The brightness the profile sets on each display, in the order the displays were saved.</summary>
    public IReadOnlyList<int?> Levels { get; }

    /// <summary>True while the displays are still set the way this profile set them.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
            {
                return;
            }

            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public string EditName
    {
        get => _editName;
        set
        {
            if (string.Equals(_editName, value, StringComparison.Ordinal))
            {
                return;
            }

            _editName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveName));
        }
    }

    public bool CanSaveName => !string.IsNullOrWhiteSpace(EditName);

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
