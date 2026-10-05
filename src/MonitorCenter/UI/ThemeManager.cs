using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MonitorCenter.Interop;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace MonitorCenter.UI;

internal static class ThemeManager
{
    private static bool _initialized;
    private static bool _isHighContrast;

    internal static bool IsLightTheme { get; private set; }
    internal static event EventHandler? ThemeChanged;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySystemTheme();
    }

    internal static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _initialized = false;
    }

    internal static void ApplySystemTheme() =>
        ApplyPalette(ReadLightTheme(), SystemParameters.HighContrast);

    internal static void ApplyPalette(bool lightTheme, bool highContrast = false)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.BeginInvoke(() => ApplyPalette(lightTheme, highContrast));
            return;
        }

        IsLightTheme = lightTheme;
        _isHighContrast = highContrast;
        var colors = highContrast ? CreateHighContrastPalette() : CreatePalette(lightTheme);
        foreach (var (key, color) in colors)
        {
            application.Resources[key] = CreateBrush(color);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void ApplyWindowTheme(Window window, int backdropType)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = IsLightTheme ? 0 : 1;
        var rounded = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref dark,
            sizeof(int));
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref rounded,
            sizeof(int));
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdropType,
            sizeof(int));
    }

    private static Dictionary<string, MediaColor> CreatePalette(bool lightTheme) => lightTheme
        ? new Dictionary<string, MediaColor>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = MediaColor.FromRgb(243, 245, 248),
            ["SidebarBackgroundBrush"] = MediaColor.FromRgb(232, 237, 243),
            ["SurfaceBrush"] = MediaColor.FromRgb(247, 249, 252),
            ["CardBackgroundBrush"] = MediaColors.White,
            ["CardHoverBrush"] = MediaColor.FromRgb(238, 242, 247),
            ["InputBackgroundBrush"] = MediaColors.White,
            ["AlternateBackgroundBrush"] = MediaColor.FromRgb(247, 249, 252),
            ["PrimaryForegroundBrush"] = MediaColor.FromRgb(24, 32, 42),
            ["SecondaryForegroundBrush"] = MediaColor.FromRgb(73, 84, 98),
            ["DisabledForegroundBrush"] = MediaColor.FromRgb(112, 123, 137),
            ["BorderBrush"] = MediaColor.FromRgb(199, 207, 218),
            ["StrongBorderBrush"] = MediaColor.FromRgb(143, 155, 170),
            ["AccentBrush"] = MediaColor.FromRgb(0, 120, 212),
            ["AccentHoverBrush"] = MediaColor.FromRgb(16, 110, 190),
            ["AccentPressedBrush"] = MediaColor.FromRgb(0, 90, 158),
            ["AccentForegroundBrush"] = MediaColors.White,
            ["SelectionBrush"] = MediaColor.FromArgb(38, 0, 120, 212),
            ["ErrorBrush"] = MediaColor.FromRgb(180, 42, 30),
            ["WarningBrush"] = MediaColor.FromRgb(139, 94, 0),
            ["SuccessBrush"] = MediaColor.FromRgb(16, 124, 65),
            ["OverlayBackgroundBrush"] = MediaColor.FromArgb(245, 247, 249, 252),
            ["OverlaySurfaceBrush"] = MediaColor.FromArgb(235, 238, 242, 247),
            ["TileBackgroundBrush"] = MediaColor.FromRgb(230, 235, 241),
            ["TileHoverBrush"] = MediaColor.FromRgb(217, 225, 234),
            ["TileDisabledBrush"] = MediaColor.FromRgb(233, 236, 240),
            ["TileFillBrush"] = MediaColor.FromArgb(190, 0, 120, 212),
            ["TileDisabledFillBrush"] = MediaColor.FromArgb(80, 112, 123, 137),
            ["TileFillEdgeBrush"] = MediaColor.FromRgb(0, 90, 158)
        }
        : new Dictionary<string, MediaColor>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = MediaColor.FromRgb(23, 26, 31),
            ["SidebarBackgroundBrush"] = MediaColor.FromRgb(17, 20, 26),
            ["SurfaceBrush"] = MediaColor.FromRgb(29, 33, 40),
            ["CardBackgroundBrush"] = MediaColor.FromRgb(35, 40, 48),
            ["CardHoverBrush"] = MediaColor.FromRgb(43, 49, 59),
            ["InputBackgroundBrush"] = MediaColor.FromRgb(27, 31, 38),
            ["AlternateBackgroundBrush"] = MediaColor.FromRgb(40, 46, 55),
            ["PrimaryForegroundBrush"] = MediaColor.FromRgb(247, 249, 252),
            ["SecondaryForegroundBrush"] = MediaColor.FromRgb(194, 202, 213),
            ["DisabledForegroundBrush"] = MediaColor.FromRgb(137, 147, 161),
            ["BorderBrush"] = MediaColor.FromRgb(65, 73, 86),
            ["StrongBorderBrush"] = MediaColor.FromRgb(104, 116, 133),
            ["AccentBrush"] = MediaColor.FromRgb(47, 164, 231),
            ["AccentHoverBrush"] = MediaColor.FromRgb(85, 184, 237),
            ["AccentPressedBrush"] = MediaColor.FromRgb(36, 135, 192),
            ["AccentForegroundBrush"] = MediaColors.White,
            ["SelectionBrush"] = MediaColor.FromArgb(85, 53, 169, 232),
            ["ErrorBrush"] = MediaColor.FromRgb(255, 180, 171),
            ["WarningBrush"] = MediaColor.FromRgb(243, 194, 118),
            ["SuccessBrush"] = MediaColor.FromRgb(114, 212, 147),
            ["OverlayBackgroundBrush"] = MediaColor.FromArgb(242, 26, 30, 36),
            ["OverlaySurfaceBrush"] = MediaColor.FromArgb(232, 42, 48, 57),
            ["TileBackgroundBrush"] = MediaColor.FromRgb(52, 58, 68),
            ["TileHoverBrush"] = MediaColor.FromRgb(64, 72, 84),
            ["TileDisabledBrush"] = MediaColor.FromRgb(42, 47, 55),
            ["TileFillBrush"] = MediaColor.FromArgb(201, 47, 164, 231),
            ["TileDisabledFillBrush"] = MediaColor.FromArgb(85, 77, 89, 103),
            ["TileFillEdgeBrush"] = MediaColor.FromRgb(118, 200, 242)
        };

    private static Dictionary<string, MediaColor> CreateHighContrastPalette()
    {
        var window = System.Windows.SystemColors.WindowColor;
        var foreground = System.Windows.SystemColors.WindowTextColor;
        var control = System.Windows.SystemColors.ControlColor;
        var controlText = System.Windows.SystemColors.ControlTextColor;
        var highlight = System.Windows.SystemColors.HighlightColor;
        var highlightText = System.Windows.SystemColors.HighlightTextColor;
        return new Dictionary<string, MediaColor>(StringComparer.Ordinal)
        {
            ["WindowBackgroundBrush"] = window,
            ["SidebarBackgroundBrush"] = control,
            ["SurfaceBrush"] = window,
            ["CardBackgroundBrush"] = window,
            ["CardHoverBrush"] = control,
            ["InputBackgroundBrush"] = window,
            ["AlternateBackgroundBrush"] = control,
            ["PrimaryForegroundBrush"] = foreground,
            ["SecondaryForegroundBrush"] = foreground,
            ["DisabledForegroundBrush"] = System.Windows.SystemColors.GrayTextColor,
            ["BorderBrush"] = controlText,
            ["StrongBorderBrush"] = foreground,
            ["AccentBrush"] = highlight,
            ["AccentHoverBrush"] = highlight,
            ["AccentPressedBrush"] = highlight,
            ["AccentForegroundBrush"] = highlightText,
            ["SelectionBrush"] = highlight,
            ["ErrorBrush"] = foreground,
            ["WarningBrush"] = foreground,
            ["SuccessBrush"] = foreground,
            ["OverlayBackgroundBrush"] = window,
            ["OverlaySurfaceBrush"] = control,
            ["TileBackgroundBrush"] = control,
            ["TileHoverBrush"] = highlight,
            ["TileDisabledBrush"] = control,
            ["TileFillBrush"] = highlight,
            ["TileDisabledFillBrush"] = System.Windows.SystemColors.GrayTextColor,
            ["TileFillEdgeBrush"] = highlightText
        };
    }

    private static SolidColorBrush CreateBrush(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool ReadLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // This event also fires for wallpaper, mouse, keyboard, and many other unrelated settings.
        if (e.Category is not (UserPreferenceCategory.General or
            UserPreferenceCategory.Color or
            UserPreferenceCategory.VisualStyle or
            UserPreferenceCategory.Accessibility))
        {
            return;
        }

        var application = System.Windows.Application.Current;
        application?.Dispatcher.BeginInvoke(ApplySystemThemeIfChanged);
    }

    private static void ApplySystemThemeIfChanged()
    {
        var lightTheme = ReadLightTheme();
        var highContrast = SystemParameters.HighContrast;
        // High contrast palettes come from system colors, which can change without the mode flag changing.
        if (!highContrast && !_isHighContrast && lightTheme == IsLightTheme)
        {
            return;
        }

        ApplyPalette(lightTheme, highContrast);
    }
}
