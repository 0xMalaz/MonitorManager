using System.Globalization;

namespace MonitorCenter.Localization;

/// <summary>
/// User-facing text in English and Turkish. Turkish is shown when the Windows display language is Turkish and English
/// otherwise; there is no in-app language setting. Technical names (DDC/CI, MonitorCenter-Setup.exe) stay untranslated,
/// and low-level diagnostic messages from Windows calls are left in English.
/// </summary>
/// <remarks>Public so XAML can bind to it with <c>x:Static</c>.</remarks>
public static class Strings
{
    internal static bool IsTurkish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

    private static string T(string english, string turkish) => IsTurkish ? turkish : english;

    // Flyout
    public static string Displays => T("Displays", "Ekranlar");
    public static string RefreshDisplays => T("Refresh displays", "Ekranları yenile");
    public static string ProfilesHeader => T("PROFILES", "PROFİLLER");
    public static string NewProfile => T("New Profile", "Yeni Profil");
    public static string ApplyProfile => T("Apply profile", "Profili uygula");
    public static string ProfileName => T("Profile name", "Profil adı");
    public static string NewProfileName => T("New profile name", "Yeni profil adı");
    public static string NewProfileHint => T(
        "Name this profile and save the current display setup.",
        "Bu profile bir ad verin ve geçerli ekran düzenini kaydedin.");
    public static string Cancel => T("Cancel", "İptal");
    public static string Save => T("Save", "Kaydet");
    public static string SaveName => T("Save name", "Adı kaydet");
    public static string Options => T("Options", "Seçenekler");
    public static string Rename => T("Rename", "Yeniden adlandır");
    public static string Delete => T("Delete", "Sil");
    public static string DisplayName => T("Display name", "Ekran adı");
    public static string DetectingDisplays => T("Detecting displays...", "Ekranlar algılanıyor...");

    internal static string Profile => T("Profile", "Profil");
    internal static string NumberedProfile(int index) => T($"Profile {index}", $"Profil {index}");

    // Display and profile messages
    internal static string NoDisplaysDetected => T("No displays detected.", "Ekran algılanamadı.");
    internal static string DisplayDetectionFailed(string reason) =>
        T($"Display detection failed: {reason}", $"Ekranlar algılanamadı: {reason}");
    internal static string TooManyProfiles =>
        T("You can save up to three profiles.", "En fazla üç profil kaydedebilirsiniz.");
    internal static string DuplicateProfileName =>
        T("A profile with that name already exists.", "Bu adda bir profil zaten var.");
    internal static string EnterProfileName => T("Enter a profile name.", "Bir profil adı girin.");
    internal static string ProfileNoLongerExists => T("That profile no longer exists.", "Bu profil artık yok.");
    internal static string EnterDisplayName => T("Enter a display name.", "Bir ekran adı girin.");
    internal static string NoControllableDisplays => T(
        "No controllable displays are available to save.",
        "Kaydedilecek, parlaklığı ayarlanabilen bir ekran yok.");
    internal static string SettingsUnreadable => T(
        "MonitorCenter couldn't read its settings file, so it won't overwrite it. Restart MonitorCenter to save changes.",
        "MonitorCenter ayar dosyasını okuyamadığı için üzerine yazmayacak. Değişiklikleri kaydetmek için MonitorCenter'ı yeniden başlatın.");

    // Display tiles
    internal static string Percent(int value) => T($"{value}%", $"%{value}");
    internal static string ReconnectingShort => T("Reconnecting…", "Yeniden bağlanıyor…");
    internal static string Reconnecting => T("Reconnecting", "Yeniden bağlanıyor");
    internal static string TryAgain => T("Try again", "Yeniden deneyin");
    internal static string Unavailable => T("Unavailable", "Kullanılamıyor");
    internal static string Unknown => T("Unknown", "Bilinmiyor");
    internal static string WindowsBuiltInControl =>
        T("Windows built-in display control", "Windows yerleşik ekran denetimi");
    internal static string Serial => T("Serial", "Seri no");
    internal static string Control => T("Control", "Denetim");
    internal static string DisplayCannotBeControlled =>
        T("This display can't be controlled.", "Bu ekranın parlaklığı ayarlanamıyor.");
    internal static string CouldNotSetBrightness => T(
        "Could not set brightness. Use Refresh displays and try again.",
        "Parlaklık ayarlanamadı. Ekranları yenileyip yeniden deneyin.");
    internal static string ReconnectingToDisplay => T("Reconnecting to the display…", "Ekrana yeniden bağlanılıyor…");
    internal static string BrightnessUnavailable => T(
        "Brightness unavailable — check DDC/CI and the display cable.",
        "Parlaklık kullanılamıyor — DDC/CI ayarını ve ekran kablosunu kontrol edin.");
    internal static string DisplayDisconnected => T("The display is no longer connected.", "Ekran artık bağlı değil.");
    internal static string BuiltInDisplayUnavailable =>
        T("The built-in display is no longer available.", "Dahili ekran artık kullanılamıyor.");
    internal static string BrightnessMismatch(int actual, int requested) => T(
        $"The display reported {actual}% after {requested}% was requested.",
        $"%{requested} istendi ancak ekran %{actual} bildirdi.");
    internal static string BrightnessNotConfirmed => T(
        "The display did not confirm its brightness change.",
        "Ekran parlaklık değişikliğini onaylamadı.");

    // Tray icon and notifications
    internal static string CheckForUpdates => T("Check for updates", "Güncellemeleri denetle");
    internal static string InstallUpdate(string version) =>
        T($"Install update ({version})", $"Güncellemeyi yükle ({version})");
    internal static string StartWithWindows => T("Start with Windows", "Windows ile başlat");
    internal static string Exit => T("Exit", "Çıkış");
    internal static string StartWithWindowsFailed =>
        T("Start with Windows could not be enabled.", "Windows ile başlatma etkinleştirilemedi.");
    internal static string StartupUpdateFailed =>
        T("Windows startup could not be updated.", "Windows ile başlatma ayarı değiştirilemedi.");
    internal static string UpdatedToVersion(string version) =>
        T($"MonitorCenter was updated to version {version}.", $"MonitorCenter {version} sürümüne güncellendi.");
    internal static string InitializationFailed(string reason) =>
        T($"MonitorCenter could not initialize: {reason}", $"MonitorCenter başlatılamadı: {reason}");
    internal static string UpdateAvailable(string version) => T(
        $"MonitorCenter {version} is available. Click here or use the tray menu to install it.",
        $"MonitorCenter {version} sürümü hazır. Yüklemek için buraya tıklayın veya tepsi menüsünü kullanın.");

    // Update window
    internal static string UpdatesTitle => T("MonitorCenter updates", "MonitorCenter güncellemeleri");
    internal static string UpdatesUnavailable => T(
        "Updates are unavailable in development or unpackaged builds. Install MonitorCenter-Setup.exe first.",
        "Geliştirme veya paketlenmemiş sürümlerde güncelleme yapılamaz. Önce MonitorCenter-Setup.exe ile yükleyin.");
    internal static string CheckingForUpdates => T("Checking for updates…", "Güncellemeler denetleniyor…");
    internal static string UpToDate => T("MonitorCenter is up to date.", "MonitorCenter güncel.");
    internal static string RestartingToUpdate =>
        T("Restarting to apply the update…", "Güncellemeyi uygulamak için yeniden başlatılıyor…");
    internal static string DownloadStarting(string version) => T($"Downloading {version}…", $"{version} indiriliyor…");
    internal static string DownloadProgress(string version, int percent) =>
        T($"Downloading {version}: {percent}%", $"{version} indiriliyor: %{percent}");
    internal static string DownloadComplete(string version) =>
        T($"Downloaded {version}: 100%", $"{version} indirildi: %100");
    internal static string UpdateFailed(string reason) => T(
        $"Update failed: {reason}\nTry again from the tray menu.",
        $"Güncelleme başarısız oldu: {reason}\nTepsi menüsünden yeniden deneyin.");
}
