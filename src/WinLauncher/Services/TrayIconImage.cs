using Drawing = System.Drawing;

namespace WinLauncher.Services;

/// <summary>
/// 托盘图标。优先从 exe 自身内嵌的图标里取 ——
/// 这样改了 Assets\app.ico 之后托盘会自动跟着变，不用维护两份资源。
/// </summary>
internal static class TrayIconImage
{
    private static Drawing.Icon? _cached;

    public static Drawing.Icon Current => _cached ??= Load();

    private static Drawing.Icon Load()
    {
        try
        {
            // 单文件发布下 Assembly.Location 是空串，必须用 ProcessPath
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(executable))
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(executable);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // 取不到就用系统默认图标兜底
        }

        return Drawing.SystemIcons.Application;
    }
}
