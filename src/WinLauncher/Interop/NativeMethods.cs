using System.Runtime.InteropServices;

namespace WinLauncher.Interop;

internal static class NativeMethods
{
    // ==================== DWM：Mica 材质与窗口圆角 ====================
    //
    // 已用反射核实：.NET 10 的 System.Windows.Window 并没有 SystemBackdrop /
    // CornerRadius 属性（PresentationFramework 里只有内部使用的 WindowBackdropType
    // 枚举），所以只能走 DwmSetWindowAttribute。

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    /// <summary>Mica，桌面壁纸采样，适合主窗口。</summary>
    public const int DWMSBT_MAINWINDOW = 2;
    /// <summary>Acrylic，适合临时/浮层窗口。</summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;
    /// <summary>Mica Alt，对比度更高。</summary>
    public const int DWMSBT_TABBEDWINDOW = 4;

    /// <summary>
    /// 返回 0 表示成功。在不支持的系统（Win10、部分远程桌面会话）上返回非 0，
    /// 调用方应借此降级为纯色背景而不是崩溃。
    /// </summary>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // ==================== GDI ====================

    /// <summary>释放 IShellItemImageFactory 返回的 HBITMAP，漏掉会持续泄漏 GDI 句柄。</summary>
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    // ==================== Shell：任意 item 的图标提取 ====================

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE(int cx, int cy)
    {
        public int cx = cx;
        public int cy = cy;
    }

    [Flags]
    public enum SIIGBF
    {
        RESIZETOFIT = 0x00,
        BIGGERSIZEOK = 0x01,
        MEMORYONLY = 0x02,
        /// <summary>只要图标，不要让 shell 去生成缩略图。</summary>
        ICONONLY = 0x04,
        THUMBNAILONLY = 0x08,
        ICONBACKGROUND = 0x10,
        SCALEUP = 0x20,
    }

    /// <summary>
    /// 资源管理器自己用的图标接口。相比 ExtractAssociatedIcon（只有 32px）
    /// 和 ExtractIconEx（拿不到 .lnk 自定义图标 / .url 关联图标），
    /// 它对任意 shell item 都返回**资源管理器实际显示的那张图**，视觉天然一致。
    /// </summary>
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    // ==================== 窗口消息：全局热键 ====================

    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    /// <summary>按住不放时不重复触发。</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
