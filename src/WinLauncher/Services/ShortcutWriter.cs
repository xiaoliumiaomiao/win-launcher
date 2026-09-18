using System.Runtime.InteropServices;
using WinLauncher.Interop;

namespace WinLauncher.Services;

/// <summary>
/// 创建 .lnk 快捷方式。
///
/// 为什么不用 WScript.Shell（PowerShell 里最省事的那个办法）：
/// 它是 ANSI 时代的 COM 组件，在"非 Unicode 程序的语言"被设成西欧(1252) 的系统上
/// **但凡路径里有中文就写不了** —— 中文会变成 "???" 然后 Save 直接失败。
/// 而这个程序的目标路径、快捷方式名几乎必然含中文。
///
/// IShellLinkW 是 Unicode 原生的，没有这个问题。
/// （读 .lnk 的对应实现见 <see cref="ShortcutResolver"/>。）
/// </summary>
public static class ShortcutWriter
{
    /// <summary>桌面快捷方式的默认名字。</summary>
    public const string DefaultShortcutName = "应用启动器";

    /// <summary>
    /// 建一个指向当前程序的桌面快捷方式。
    /// 路径用 SpecialFolder.DesktopDirectory，不是硬编码 %USERPROFILE%\Desktop ——
    /// 开了 OneDrive 桌面同步的话后者会找不到。
    /// </summary>
    public static string DesktopShortcutPath(string name = DefaultShortcutName)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            name + ".lnk");

    public static bool TryCreate(
        string shortcutPath,
        string targetPath,
        string? description,
        string? workingDirectory,
        out string? error)
    {
        error = null;
        object? shellLinkObject = null;
        object? persistFileObject = null;

        try
        {
            var directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            shellLinkObject = ShellLinkInterop.CreateShellLinkInstance();
            var link = (IShellLinkW)shellLinkObject;

            link.SetPath(targetPath);

            var workDirectory = workingDirectory ?? Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(workDirectory))
                link.SetWorkingDirectory(workDirectory);

            if (!string.IsNullOrEmpty(description))
                link.SetDescription(description);

            // 图标取目标程序自己的，这样桌面图标和程序一致
            link.SetIconLocation(targetPath, 0);

            persistFileObject = shellLinkObject;
            var persist = (IPersistFile)persistFileObject;

            // fRemember = true。为 false 时 .lnk 是"未命名"状态，资源管理器里会显示异常
            persist.Save(shortcutPath, true);

            Diagnostics.Log($"已创建快捷方式：{shortcutPath} → {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Diagnostics.Log($"创建快捷方式失败（{shortcutPath}）：{ex.Message}");
            return false;
        }
        finally
        {
            // 和 ShortcutResolver 一样：不手动释放会残留 COM 对象
            if (persistFileObject is not null && Marshal.IsComObject(persistFileObject))
                Marshal.FinalReleaseComObject(persistFileObject);

            if (shellLinkObject is not null
                && !ReferenceEquals(shellLinkObject, persistFileObject)
                && Marshal.IsComObject(shellLinkObject))
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }
    }
}
