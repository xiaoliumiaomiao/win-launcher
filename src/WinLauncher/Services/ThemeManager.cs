using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinLauncher.Interop;

namespace WinLauncher.Services;

/// <summary>
/// 跟随系统深浅色，并把 Mica 材质 / 圆角 / 深色标题栏应用到窗口上。
///
/// 已用反射确认 .NET 10 的 Window 没有 SystemBackdrop / CornerRadius 属性，
/// 只能走 DwmSetWindowAttribute。
/// </summary>
public static class ThemeManager
{
    public static bool IsDarkMode { get; private set; }

    public static void Initialize()
    {
        IsDarkMode = ReadSystemUsesDarkMode();
        ApplyBrushes(IsDarkMode);
    }

    /// <summary>
    /// 把 Mica 材质、圆角、深浅色标题栏应用到窗口。
    /// 窗口显示后（SourceInitialized 之后）才能拿到有效 HWND。
    /// </summary>
    public static void ApplyToWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var dark = IsDarkMode ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        var corner = NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var backdrop = NativeMethods.DWMSBT_MAINWINDOW;   // Mica
        var hr = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        // 返回非 0 说明系统不支持 Mica（Win10、部分远程桌面会话）。
        // 降级成不透明纯色背景，功能完全不受影响。
        if (hr != 0)
            ApplyFallbackBackground(dark == 1);
    }

    /// <summary>窗口被拖到另一块缩放比例不同的显示器时，DWM 属性需要重新施加。</summary>
    public static void ReapplyOnDpiChange(Window window)
        => ApplyToWindow(window);

    private static void ApplyFallbackBackground(bool dark)
    {
        SetBrushColor("PanelBackgroundBrush", dark
            ? Color.FromRgb(0x20, 0x20, 0x20)
            : Color.FromRgb(0xF3, 0xF3, 0xF3));
    }

    private static void ApplyBrushes(bool dark)
    {
        if (dark)
        {
            // 带 alpha 的深色，让 Mica 透出来
            SetBrushColor("PanelBackgroundBrush", Color.FromArgb(0xCC, 0x1F, 0x1F, 0x1F));
            SetBrushColor("SidebarBackgroundBrush", Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            SetBrushColor("TitleBarBackgroundBrush", Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            SetBrushColor("CardBackgroundBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CardHoverBrush", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CardPressedBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            SetBrushColor("IconTileBrush", Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CategorySelectedBrush", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CategoryHoverBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            SetBrushColor("TextPrimaryBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetBrushColor("TextSecondaryBrush", Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));
            SetBrushColor("TextTertiaryBrush", Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            SetBrushColor("AccentBrush", Color.FromRgb(0x60, 0xCD, 0xFF));
            SetBrushColor("DividerBrush", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            SetBrushColor("SearchBackgroundBrush", Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            SetBrushColor("DangerBrush", Color.FromRgb(0xFF, 0x72, 0x72));
            SetBrushColor("DialogBackgroundBrush", Color.FromArgb(0xF2, 0x1F, 0x1F, 0x1F));
        }
        else
        {
            SetBrushColor("PanelBackgroundBrush", Color.FromArgb(0xCC, 0xF3, 0xF3, 0xF3));
            SetBrushColor("SidebarBackgroundBrush", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            SetBrushColor("TitleBarBackgroundBrush", Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            SetBrushColor("CardBackgroundBrush", Color.FromArgb(0x7A, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CardHoverBrush", Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
            SetBrushColor("CardPressedBrush", Color.FromArgb(0x5A, 0xFF, 0xFF, 0xFF));
            SetBrushColor("IconTileBrush", Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
            SetBrushColor("CategorySelectedBrush", Color.FromArgb(0x40, 0x00, 0x00, 0x00));
            SetBrushColor("CategoryHoverBrush", Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
            SetBrushColor("TextPrimaryBrush", Color.FromRgb(0x1A, 0x1A, 0x1A));
            SetBrushColor("TextSecondaryBrush", Color.FromArgb(0x9E, 0x00, 0x00, 0x00));
            SetBrushColor("TextTertiaryBrush", Color.FromArgb(0x66, 0x00, 0x00, 0x00));
            SetBrushColor("AccentBrush", Color.FromRgb(0x00, 0x5F, 0xB8));
            SetBrushColor("DividerBrush", Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            SetBrushColor("SearchBackgroundBrush", Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            SetBrushColor("DangerBrush", Color.FromRgb(0xC4, 0x2B, 0x1C));
            SetBrushColor("DialogBackgroundBrush", Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFA));
        }
    }

    /// <summary>
    /// 初始化阶段替换被冻结的画刷；窗口创建后仍可修改未冻结的画刷。
    /// WPF 会冻结编译后的 XAML 画刷资源，直接给 Color 赋值会被跳过。
    /// 界面以 DynamicResource 引用这些资源，替换后可重绘现有控件。
    /// </summary>
    private static void SetBrushColor(string resourceKey, Color color)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        if (resources[resourceKey] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            resources[resourceKey] = new SolidColorBrush(color);
    }

    private static bool ReadSystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // AppsUseLightTheme: 0 = 深色, 1 = 浅色；键不存在时按浅色处理
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
