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
            ["OverlayBackgroundBrush"] = MediaColor.FromArgb(247, 243, 243, 243),
            ["FlyoutDividerBrush"] = MediaColor.FromRgb(224, 224, 224),
            ["FlyoutSectionHeaderBrush"] = MediaColor.FromRgb(95, 95, 95),
            ["FlyoutGlyphBrush"] = MediaColor.FromRgb(95, 95, 95),
            ["FlyoutSubtleHoverBrush"] = MediaColor.FromArgb(15, 0, 0, 0),
            ["FlyoutSubtlePressedBrush"] = MediaColor.FromArgb(10, 0, 0, 0),
            ["TileMenuBackgroundBrush"] = MediaColor.FromRgb(249, 249, 249),
            ["TileMenuBorderBrush"] = MediaColor.FromRgb(224, 224, 224),
            ["OutlineButtonBorderBrush"] = MediaColor.FromRgb(208, 208, 208),
            ["OutlineButtonHoverBrush"] = MediaColor.FromRgb(234, 234, 234),
            ["OutlineButtonDisabledBorderBrush"] = MediaColor.FromRgb(227, 227, 227),
            ["OutlineButtonDisabledForegroundBrush"] = MediaColor.FromRgb(160, 160, 160),
            ["ProfileCardBrush"] = MediaColor.FromRgb(251, 251, 251),
            ["ProfileCardHoverBrush"] = MediaColor.FromRgb(246, 246, 246),
            ["ProfileCardPressedBrush"] = MediaColor.FromRgb(240, 240, 240),
            ["ProfileCardBorderBrush"] = MediaColor.FromRgb(226, 226, 226),
            ["ProfileCardActiveBorderBrush"] = MediaColor.FromRgb(0, 120, 212),
            ["ProfileCardActiveTintBrush"] = MediaColor.FromArgb(18, 0, 120, 212),
            ["ProfileLevelTrackBrush"] = MediaColor.FromRgb(227, 227, 227),
            ["ProfileLevelFillBrush"] = MediaColor.FromRgb(0, 120, 212),
            ["MonitorFrameBrush"] = MediaColor.FromRgb(196, 196, 196),
            ["MonitorFrameHoverBrush"] = MediaColor.FromRgb(171, 171, 171),
            ["MonitorBezelBrush"] = MediaColor.FromRgb(28, 28, 28),
            ["MonitorScreenBrush"] = MediaColor.FromRgb(42, 42, 42),
            ["MonitorFillTopBrush"] = MediaColor.FromRgb(128, 212, 255),
            ["MonitorFillBottomBrush"] = MediaColor.FromRgb(42, 135, 216),
            ["MonitorDisabledFillBrush"] = MediaColor.FromRgb(85, 85, 85),
            ["MonitorLabelBackgroundBrush"] = MediaColor.FromArgb(184, 12, 12, 12),
            ["MonitorLabelForegroundBrush"] = MediaColors.White,
            ["MonitorStatusForegroundBrush"] = MediaColor.FromRgb(255, 180, 171)
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
            ["OverlayBackgroundBrush"] = MediaColor.FromArgb(247, 32, 32, 32),
            ["FlyoutDividerBrush"] = MediaColor.FromRgb(45, 45, 45),
            ["FlyoutSectionHeaderBrush"] = MediaColor.FromRgb(168, 168, 168),
            ["FlyoutGlyphBrush"] = MediaColor.FromRgb(189, 189, 189),
            ["FlyoutSubtleHoverBrush"] = MediaColor.FromArgb(20, 255, 255, 255),
            ["FlyoutSubtlePressedBrush"] = MediaColor.FromArgb(14, 255, 255, 255),
            ["TileMenuBackgroundBrush"] = MediaColor.FromRgb(44, 44, 44),
            ["TileMenuBorderBrush"] = MediaColor.FromRgb(66, 66, 66),
            ["OutlineButtonBorderBrush"] = MediaColor.FromRgb(61, 61, 61),
            ["OutlineButtonHoverBrush"] = MediaColor.FromRgb(45, 45, 45),
            ["OutlineButtonDisabledBorderBrush"] = MediaColor.FromRgb(45, 45, 45),
            ["OutlineButtonDisabledForegroundBrush"] = MediaColor.FromRgb(92, 92, 92),
            ["ProfileCardBrush"] = MediaColor.FromRgb(43, 43, 43),
            ["ProfileCardHoverBrush"] = MediaColor.FromRgb(50, 50, 50),
            ["ProfileCardPressedBrush"] = MediaColor.FromRgb(39, 39, 39),
            ["ProfileCardBorderBrush"] = MediaColor.FromRgb(57, 57, 57),
            ["ProfileCardActiveBorderBrush"] = MediaColor.FromRgb(96, 205, 255),
            ["ProfileCardActiveTintBrush"] = MediaColor.FromArgb(22, 96, 205, 255),
            ["ProfileLevelTrackBrush"] = MediaColor.FromRgb(61, 61, 61),
            ["ProfileLevelFillBrush"] = MediaColor.FromRgb(96, 205, 255),
            ["MonitorFrameBrush"] = MediaColor.FromRgb(60, 60, 60),
            ["MonitorFrameHoverBrush"] = MediaColor.FromRgb(82, 82, 82),
            ["MonitorBezelBrush"] = MediaColor.FromRgb(12, 12, 12),
            ["MonitorScreenBrush"] = MediaColor.FromRgb(21, 21, 21),
            ["MonitorFillTopBrush"] = MediaColor.FromRgb(128, 212, 255),
            ["MonitorFillBottomBrush"] = MediaColor.FromRgb(42, 135, 216),
            ["MonitorDisabledFillBrush"] = MediaColor.FromRgb(58, 58, 58),
            ["MonitorLabelBackgroundBrush"] = MediaColor.FromArgb(184, 12, 12, 12),
            ["MonitorLabelForegroundBrush"] = MediaColors.White,
            ["MonitorStatusForegroundBrush"] = MediaColor.FromRgb(255, 180, 171)
        };

    private static Dictionary<string, MediaColor> CreateHighContrastPalette()
    {
        var window = System.Windows.SystemColors.WindowColor;
        var foreground = System.Windows.SystemColors.WindowTextColor;
        var control = System.Windows.SystemColors.ControlColor;
        var controlText = System.Windows.SystemColors.ControlTextColor;
        var highlight = System.Windows.SystemColors.HighlightColor;
        var highlightText = System.Windows.SystemColors.HighlightTextColor;
        var grayText = System.Windows.SystemColors.GrayTextColor;
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
            ["DisabledForegroundBrush"] = grayText,
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
            ["FlyoutDividerBrush"] = foreground,
            ["FlyoutSectionHeaderBrush"] = foreground,
            ["FlyoutGlyphBrush"] = foreground,
            ["FlyoutSubtleHoverBrush"] = control,
            ["FlyoutSubtlePressedBrush"] = control,
            ["TileMenuBackgroundBrush"] = window,
            ["TileMenuBorderBrush"] = controlText,
            ["OutlineButtonBorderBrush"] = controlText,
            ["OutlineButtonHoverBrush"] = control,
            ["OutlineButtonDisabledBorderBrush"] = grayText,
            ["OutlineButtonDisabledForegroundBrush"] = grayText,
            ["ProfileCardBrush"] = window,
            ["ProfileCardHoverBrush"] = control,
            ["ProfileCardPressedBrush"] = control,
            ["ProfileCardBorderBrush"] = controlText,
            ["ProfileCardActiveBorderBrush"] = highlight,
            ["ProfileCardActiveTintBrush"] = MediaColors.Transparent,
            ["ProfileLevelTrackBrush"] = control,
            ["ProfileLevelFillBrush"] = highlight,
            ["MonitorFrameBrush"] = controlText,
            ["MonitorFrameHoverBrush"] = highlight,
            ["MonitorBezelBrush"] = window,
            ["MonitorScreenBrush"] = window,
            ["MonitorFillTopBrush"] = highlight,
            ["MonitorFillBottomBrush"] = highlight,
            ["MonitorDisabledFillBrush"] = grayText,
            ["MonitorLabelBackgroundBrush"] = window,
            ["MonitorLabelForegroundBrush"] = foreground,
            ["MonitorStatusForegroundBrush"] = foreground
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
