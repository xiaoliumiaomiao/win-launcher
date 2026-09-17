using System.Text;

namespace WinLauncher.Services;

/// <summary>
/// 轻量诊断日志。
///
/// 拖放这条链路有个讨厌的性质：失败时**完全静默** —— 没有异常、没有返回值、
/// 没有系统提示。出问题只能靠日志反推，所以这里常开。
///
/// 只记关键节点，文件超过 256KB 自动截断重来，不会无限增长。
/// </summary>
public static class Diagnostics
{
    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(AppPaths.DataRoot, "diagnostics.log");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();

                var file = new FileInfo(LogPath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Delete(LogPath);

                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志本身绝不能影响功能
        }
    }

    /// <summary>记录一次拖放事件的概要，用于排查"拖不进去"。</summary>
    public static void LogDrag(string stage, bool hasFileDrop, int fileCount, string? firstFile, string effects)
        => Log($"[拖放] {stage} | "
               + (hasFileDrop
                   ? $"FileDrop={fileCount}个 第一个={firstFile} Effects={effects}"
                   : $"不含 FileDrop 格式（可能来自开始菜单的虚拟 shell 文件夹）Effects={effects}"));
}
