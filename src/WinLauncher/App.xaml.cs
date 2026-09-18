using System.Windows;
using WinLauncher.Services;
using WinLauncher.ViewModels;

namespace WinLauncher;

public partial class App : Application
{
    // Local\ 前缀 = 只在当前登录会话内唯一，正是桌面应用想要的语义
    private const string MutexName = @"Local\WinLauncher.SingleInstance.4B1E";
    private const string ShowEventName = @"Local\WinLauncher.ShowWindow.4B1E";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showWatcherCts;
    private bool _reallyExiting;

    private DataStore _store = null!;
    private IconService _icons = null!;
    private HotKeyService _hotKeys = null!;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 先建事件再抢互斥锁，这样"第二个实例通知第一个实例"的通道一定已经存在
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // 已经有实例在跑了：把它叫到前台，然后自己退出。
            // 否则重复双击桌面图标会开出第二个常驻进程，两个托盘图标、两份热键。
            SignalExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"出错了：\n\n{args.Exception.Message}\n\n程序会继续运行。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // 开机自启时带这个参数：只驻留托盘，不弹窗口
        var silent = e.Args.Any(a =>
            string.Equals(a, StartupManager.SilentArgument, StringComparison.OrdinalIgnoreCase));

        ThemeManager.Initialize();
        AppPaths.EnsureCreated();

        _store = new DataStore();
        _store.Load();

        // 校正开机自启里记的路径。放在 Load 之后 —— 它要写日志，
        // 而日志目录是 AppPaths.EnsureCreated 建的。
        StartupManager.Sync();

        _icons = new IconService();

        _hotKeys = new HotKeyService();
        _hotKeys.Pressed += ToggleWindow;

        _tray = new TrayIcon();
        _tray.ShowRequested += ShowWindow;
        _tray.ExitRequested += ExitApplication;

        _window = new MainWindow(_store, _icons, _hotKeys);
        MainWindow = _window;

        // 点 ✕ 只隐藏到托盘，程序继续常驻 —— 否则全局热键就成了摆设
        _window.Closing += (_, args) =>
        {
            if (_reallyExiting)
                return;

            args.Cancel = true;
            _window.Hide();
        };

        // ⚠️ 数据层的启动工作（首次扫描、失效检测）不能挂在窗口的 Loaded 事件上。
        // 静默自启时窗口从头到尾不显示，Loaded 根本不会触发，扫描也就不会跑 ——
        // 表现出来就是"开机自启之后应用列表一直不更新"，而且没有任何报错。
        _window.RunStartupWork();

        if (silent)
        {
            Diagnostics.Log($"静默启动（{StartupManager.SilentArgument}）：只驻留托盘，不显示窗口");
        }
        else
        {
            _window.Show();
        }

        StartShowWatcher();
    }

    private void StartShowWatcher()
    {
        _showWatcherCts = new CancellationTokenSource();
        var token = _showWatcherCts.Token;
        var handle = _showEvent!;

        // LongRunning：这是个阻塞等待的循环，不该占用线程池的工作线程
        _ = Task.Factory.StartNew(
            () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!handle.WaitOne(500))
                            continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    Dispatcher.Invoke(ShowWindow);
                }
            },
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void SignalExistingInstance()
    {
        // 极小概率下第一个实例还没把事件建好，重试几次
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ShowEventName);
                handle.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(80);
            }
            catch
            {
                return;
            }
        }
    }

    private void ShowWindow()
    {
        if (_window is null)
            return;

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        // 从热键/托盘唤起时窗口可能不是前台窗口（Windows 的前台窗口锁定机制），
        // 用一次 Topmost 切换把它顶到最前
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
        _window.FocusSearchBox();
    }

    private void ToggleWindow()
    {
        if (_window is null)
            return;

        // 只记一行。「热键没反应」这类反馈里，第一个要回答的问题就是
        // "热键到底有没有触发" —— 有这行就能区分"热键没收到"和"收到了但窗口没显示"。
        Diagnostics.Log($"热键触发：IsVisible={_window.IsVisible} IsActive={_window.IsActive}");

        if (_window.IsVisible && _window.IsActive)
            _window.Hide();
        else
            ShowWindow();
    }

    private void ExitApplication()
    {
        _reallyExiting = true;

        try { _store.Save(); } catch { /* 退出时存不下就算了，别卡住关闭 */ }

        _showWatcherCts?.Cancel();
        _showWatcherCts?.Dispose();

        _tray?.Dispose();
        _hotKeys?.Dispose();
        _icons?.Dispose();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _reallyExiting = true;

        try { _store?.Save(); } catch { /* 忽略 */ }

        _showWatcherCts?.Cancel();
        _tray?.Dispose();
        _hotKeys?.Dispose();
        _icons?.Dispose();
        _showEvent?.Dispose();

        try { _instanceMutex?.ReleaseMutex(); } catch { /* 没持有就忽略 */ }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
