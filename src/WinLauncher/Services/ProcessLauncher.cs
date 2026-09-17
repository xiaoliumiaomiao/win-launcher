using System.ComponentModel;
using System.Diagnostics;
using WinLauncher.Models;

namespace WinLauncher.Services;

public enum LaunchOutcome
{
    Started,

    /// <summary>用户在 UAC 弹窗上点了「取消」。这不是错误，不该弹报错框。</summary>
    CancelledByUser,

    NotFound,

    Failed,
}

public static class ProcessLauncher
{
    /// <summary>UAC 被用户取消时 Win32Exception 的错误码。</summary>
    private const int ERROR_CANCELLED = 1223;

    public static LaunchOutcome Launch(AppEntry entry, out string? error)
    {
        error = null;

        if (!EntryFactory.IsTargetAlive(entry))
            return LaunchOutcome.NotFound;

        try
        {
            var startInfo = BuildStartInfo(entry);
            Process.Start(startInfo);
            return LaunchOutcome.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return LaunchOutcome.CancelledByUser;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return LaunchOutcome.Failed;
        }
    }

    private static ProcessStartInfo BuildStartInfo(AppEntry entry)
    {
        var path = entry.LaunchPath;
        var extension = Path.GetExtension(path);

        // .ps1 直接交给 shell 会被记事本打开，必须显式包一层 PowerShell
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                UseShellExecute = false,
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            // 必须 true：.lnk / .url / URL / shell: 协议都靠 shell 解析。
            // 设为 false 启动 .lnk 会直接抛 Win32Exception。
            UseShellExecute = true,
            Verb = entry.RunAsAdmin ? "runas" : "open",
        };

        if (!string.IsNullOrWhiteSpace(entry.Arguments))
            startInfo.Arguments = entry.Arguments;

        var workingDirectory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        return startInfo;
    }

    /// <summary>在资源管理器里选中该文件。</summary>
    public static void RevealInExplorer(AppEntry entry)
    {
        try
        {
            var target = File.Exists(entry.StableKey) ? entry.StableKey : entry.LaunchPath;
            if (string.IsNullOrWhiteSpace(target))
                return;

            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true,
                    });
                }
            }
        }
        catch
        {
            // 打不开资源管理器不是需要打扰用户的错误
        }
    }
}
