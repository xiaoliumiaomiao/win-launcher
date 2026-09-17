using System.Diagnostics;
using System.Security.Principal;

namespace WinLauncher.Services;

/// <summary>
/// 提权检测与去提权重启。
///
/// 为什么必须防这件事：Windows 的 UIPI 会阻止低完整性进程（普通权限的 explorer）
/// 向高完整性进程发送窗口消息。WPF 的拖放走 OLE IDropTarget，属于跨进程窗口消息，
/// 所以**一旦本程序以管理员身份运行，从资源管理器/桌面拖文件进来就会静默失效**
/// —— 光标显示禁止符号，没有任何异常可捕获，也没有任何日志。
///
/// app.manifest 里写的是 asInvoker，但 asInvoker 的语义是"跟随调用者"，
/// 从提权的终端 Start-Process 启动、或右键"以管理员身份运行"都会让它变提权。
/// 所以光靠 manifest 不够，运行时还得自己发现。
/// </summary>
public static class ElevationGuard
{
    public static bool IsElevated { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 以普通权限重新启动自己，并让当前实例退出。
    ///
    /// 去提权不能直接 Process.Start —— 子进程会继承父进程的提权令牌。
    /// 走 explorer.exe 是因为 explorer 始终以普通权限运行，
    /// 由它来创建子进程就落回普通权限（这是标准的去提权手法）。
    /// </summary>
    public static bool TryRelaunchUnelevated()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{exePath}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
