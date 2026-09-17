using WinForms = System.Windows.Forms;

namespace WinLauncher.Services;

/// <summary>
/// 系统托盘图标。窗口点 ✕ 时隐藏到这里，程序继续常驻，
/// 这样全局热键才有意义。真正退出只走托盘菜单。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private bool _disposed;

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("显示启动器", null, (_, _) => ShowRequested?.Invoke());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = TrayIconImage.Current,
            Text = "应用启动器",
            Visible = true,
            ContextMenuStrip = _menu,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                ShowRequested?.Invoke();
        };
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // 某些系统禁用了气泡提示，忽略
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
