using Microsoft.Win32;

namespace WinLauncher.Services;

/// <summary>
/// 开机自启（准确说是"登录时自启"）。
///
/// 用 HKCU 下的 Run 键，这是绝大多数软件的标准做法，也是唯一不需要管理员权限的做法。
/// 这一点很重要：程序一旦提权，Windows 的 UIPI 就会拦掉资源管理器发来的拖放消息，
/// 拖拽会静默失效。Run 键启动的进程继承 explorer 的令牌，天然是普通权限。
///
/// 任务管理器 →「启动」应用 里能看到并一键禁用。
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinLauncher";

    /// <summary>自启时带的参数：只驻留托盘，不弹窗口。</summary>
    public const string SilentArgument = "--silent";

    /// <summary>注册表里是否已经有自启项。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool Enable(out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "打不开注册表的启动项位置";
                return false;
            }

            key.SetValue(ValueName, BuildCommandLine(), RegistryValueKind.String);
            Diagnostics.Log($"已启用开机自启：{BuildCommandLine()}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Diagnostics.Log($"启用开机自启失败：{ex.Message}");
            return false;
        }
    }

    public static bool Disable(out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            // throwOnMissingValue: false —— 本来就没有这项时不算错误
            key?.DeleteValue(ValueName, throwOnMissingValue: false);

            Diagnostics.Log("已关闭开机自启");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Diagnostics.Log($"关闭开机自启失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 校正注册表里的路径，让自启重新指向**当前正在运行的**这个 exe。
    ///
    /// 这个方法是必需的，不是锦上添花：你只要把 exe 挪个位置（从下载目录
    /// 挪到 D:\Tools，或者重新构建后换了目录），注册表里那条还指着旧路径，
    /// 自启就**静默失效**了 —— 没有任何提示，你只会在某天发现它不开机启动了。
    ///
    /// 校正的时机是程序每次启动。所以"挪完程序再双击一次"就能修好。
    /// </summary>
    public static void Sync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);

            // 没有这一项说明用户没开自启，什么都别做
            if (key?.GetValue(ValueName) is not string existing)
                return;

            var wanted = BuildCommandLine();
            if (string.Equals(existing, wanted, StringComparison.OrdinalIgnoreCase))
                return;

            key.SetValue(ValueName, wanted, RegistryValueKind.String);
            Diagnostics.Log($"自启路径已校正：{existing}  →  {wanted}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"自启路径校正失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 注册表里要写的命令行。
    ///
    /// ⚠️ 路径的引号不能省。Run 键的值是按命令行解析的，
    /// 路径里有空格时（`C:\Program Files\...`）不加引号会被截成
    /// `C:\Program` 加一堆"参数"，自启静默失败 —— 而且没有任何报错。
    /// </summary>
    private static string BuildCommandLine()
    {
        var executable = Environment.ProcessPath ?? "";
        return $"\"{executable}\" {SilentArgument}";
    }
}
