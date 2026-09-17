using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace DragProbe;

/// <summary>
/// 发起一次真实的 OLE 拖放：把 args[0] 这个文件从左上角的拖拽源窗口拖到屏幕坐标 (args[1], args[2])。
///
/// 存在的意义：WPF 的拖放走 OLE IDropTarget，没有真的拖拽源就没法触发，
/// 键盘和单点鼠标模拟都不行。这个探针就是那个源，让"拖拽添加"能被自动化验证。
///
/// 踩过的坑（前后卡了三轮才通，都记在这）：
///
///   1. **必须让 Dispatcher 真的跑起来**（app.Run()）。只 Show() 窗口但不跑消息循环的话，
///      窗口不处理任何输入消息、拿不到鼠标捕获，OLE 的 DoDragDrop 会**立刻**返回
///      DROPEFFECT_NONE。这个最坑 —— 现象像是"鼠标没按下去"，但用 GetAsyncKeyState
///      查会发现按键状态其实是好的，于是往完全错误的方向排查。
///
///   2. **拖拽源窗口必须在屏幕内**。放到 (-3000,-3000) 会导致拖拽循环卡死永不返回。
///
///   3. **收尾动作要用 DispatcherTimer 驱动**，不能用 sleep 完再 mouse_event 的后台线程 ——
///      时序对不上，松手事件会被丢掉。
///
///   4. 诊断窗口时 GetClassNameW / GetWindowTextW 必须写 CharSet = CharSet.Unicode，
///      否则默认按 ANSI 编组，连续读到第一个字符就截断了，表现为"类名只有 1 个字母、
///      标题全是乱码"，很容易误判成"窗口找不到"。
/// </summary>
internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "dragprobe.log");

    private static DragDropEffects _result = DragDropEffects.None;

    /// <summary>拖拽目标窗口的句柄，拖拽开始前会被叫到前台。</summary>
    private static IntPtr _targetWindow = IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// 找目标进程面积最大的可见顶层窗口。
    /// 不能用 Process.MainWindowHandle —— 对 WPF 程序它会返回内部的辅助小窗口。
    /// </summary>
    private static IntPtr FindMainWindow(uint targetPid)
    {
        var best = IntPtr.Zero;
        long bestArea = 0;

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid != targetPid || !IsWindowVisible(h))
                return true;

            GetWindowRect(h, out var rect);
            long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area <= bestArea)
                return true;

            bestArea = area;
            best = h;
            return true;
        }, IntPtr.Zero);

        return best;
    }

    /// <summary>
    /// 点到点检查某个屏幕坐标下是不是目标窗口。
    /// 不做这个检查的话，目标窗口被别的窗口盖住时，
    /// 拖放会被那个窗口接走，测试结果完全误导。
    /// </summary>
    private static string DescribeWindowAt(int x, int y)
    {
        var h = WindowFromPhysicalPoint(x, y);
        GetWindowThreadProcessId(h, out var pid);
        var match = _targetWindow != IntPtr.Zero && h == _targetWindow;
        return $"坐标({x},{y})下的窗口 PID={pid} 句柄=0x{h.ToInt64():X}"
               + (match ? " ✓ 就是目标窗口" : " ✗ 不是目标窗口，拖放会被其它窗口接走");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPhysicalPoint(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    [STAThread]
    private static int Main(string[] args)
    {
        File.WriteAllText(LogPath, "");

        if (args.Length < 3)
        {
            Log("用法: DragProbe <文件路径> <目标X> <目标Y>");
            Console.Error.WriteLine("用法: DragProbe <文件路径> <目标X> <目标Y>");
            return 2;
        }

        var file = args[0];
        if (!File.Exists(file))
        {
            Log($"找不到文件: {file}");
            Console.Error.WriteLine($"找不到文件: {file}");
            return 2;
        }

        var targetX = int.Parse(args[1]);
        var targetY = int.Parse(args[2]);

        // WPF 程序要自己把自己提升为 DPI 感知，否则坐标是虚拟化的，跟真实屏幕对不上
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        // 可选第 4 个参数：目标进程 PID。给了就在拖拽前把它叫到前台 ——
        // 否则目标窗口可能被别的窗口盖住，拖放会被那个窗口接走，
        // 结果是"看起来拖成功了但被测程序什么都没收到"。
        if (args.Length > 3 && uint.TryParse(args[3], out var targetPid))
        {
            _targetWindow = FindMainWindow(targetPid);
            Log($"0. 目标窗口 PID={targetPid} 句柄=0x{_targetWindow.ToInt64():X}");
        }

        // 拖拽源：屏幕左上角一个小方块，必须在屏幕内
        const int sourceSize = 56;
        const int startX = 8 + sourceSize / 2;
        const int startY = 8 + sourceSize / 2;

        // 看门狗：无论如何不能让这个进程挂死把测试卡住
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(15000);
            Log("!! 看门狗触发：15 秒内没有结束");
            Console.Error.WriteLine("看门狗超时");
            Environment.Exit(3);
        })
        {
            IsBackground = true,
        };
        watchdog.Start();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var source = new Window
        {
            Width = sourceSize,
            Height = sourceSize,
            Left = 8,
            Top = 8,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Background = System.Windows.Media.Brushes.MediumPurple,
            // 必须给窗口真实内容：没有 Content 的窗口不会触发 ContentRendered，
            // 而且纯 Background 的窗口在某些情况下不参与正常的输入路由。
            Content = new System.Windows.Shapes.Rectangle
            {
                Fill = System.Windows.Media.Brushes.MediumPurple,
            },
        };

        var data = new DataObject(DataFormats.FileDrop, new[] { file });

        // 用 Loaded 而不是 ContentRendered，配合一个短定时器让窗口彻底稳定下来
        source.Loaded += (_, _) =>
        {
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                RunDrag(source, data, startX, startY, targetX, targetY);
                app.Shutdown();
            };
            settle.Start();
        };

        source.Show();
        app.Run();

        var ok = _result != DragDropEffects.None;
        Console.WriteLine(ok
            ? $"成功：目标接受了拖放（{_result}）"
            : "失败：目标没有接受（Effects=None）");
        Console.WriteLine($"日志: {LogPath}");
        return ok ? 0 : 1;
    }

    private static void RunDrag(Window source, DataObject data, int startX, int startY, int targetX, int targetY)
    {
        var files = data.GetData(DataFormats.FileDrop) as string[] ?? [];
        Log($"1. 拖拽源窗口已就绪，Dispatcher 运行中");
        Log($"2. DataObject: {string.Join(",", files)}");

        var timers = new List<DispatcherTimer>();

        void Every(int ms, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
            timers.Add(timer);
        }

        // 分步移动，模拟真实拖动轨迹
        for (var step = 1; step <= 6; step++)
        {
            var t = step / 6.0;
            var x = (int)(startX + (targetX - startX) * t);
            var y = (int)(startY + (targetY - startY) * t);
            var n = step;
            Every(400 + n * 130, () =>
            {
                SetCursorPos(x, y);
                Log($"   移动 {n}/6 → ({x},{y})");
            });
        }

        Every(1400, () =>
        {
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Log("4. 已注入左键松开");
        });

        // 把目标窗口叫到前台，否则它可能被别的窗口盖住
        if (_targetWindow != IntPtr.Zero)
        {
            ShowWindow(_targetWindow, 5);   // SW_SHOW
            SetForegroundWindow(_targetWindow);
            Thread.Sleep(700);
        }

        Log(DescribeWindowAt(targetX, targetY));

        source.Activate();
        SetCursorPos(startX, startY);
        Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(150);

        Log($"3. 已在 ({startX},{startY}) 按下左键，进入拖拽循环");

        _result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);

        foreach (var timer in timers)
            timer.Stop();

        Log($"5. DoDragDrop 返回: {_result}");
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
        catch
        {
            // 日志写不了不影响测试本身
        }
    }
}
