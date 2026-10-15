using System.ComponentModel;
using System.Runtime.CompilerServices;
using MonitorCenter.Models;

namespace MonitorCenter.UI;

/// <summary>A profile button in the flyout, with the state of its inline rename.</summary>
internal sealed class ProfileTileViewModel : INotifyPropertyChanged
{
    private bool _isRenaming;
    private string _editName = string.Empty;

    public ProfileTileViewModel(BrightnessProfile profile)
    {
        Profile = profile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BrightnessProfile Profile { get; }
    public string Name => Profile.Name;

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
