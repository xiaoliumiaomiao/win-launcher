<#
    截取 WinLauncher 主窗口，用于开发期视觉验证。

    用法：pwsh -File tools\screenshot.ps1 [-Out 输出路径] [-ProcessName WinLauncher]

    ⚠️ DPI 陷阱：PowerShell 默认是"DPI 不感知"进程，在这种进程里
    GetWindowRect 和 System.Windows.Forms.Screen 拿到的都是被系统虚拟化过的坐标
    （在 125% 缩放屏上，2560x1440 会被报成 2048x1152）。
    所以脚本开头必须先把自己提升为 PerMonitorV2，否则截出来的图和窗口对不上。
#>
param(
    [string]$Out = "$env:TEMP\winlauncher-shot.png",
    [string]$ProcessName = 'WinLauncher',
    [int]$WaitMs = 1200
)

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinShot {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private delegate bool EnumProc(IntPtr hWnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr param);

    /// <summary>
    /// 找进程的主窗口。
    ///
    /// ⚠️ 不能用 Process.MainWindowHandle —— 它会返回 WPF 内部那些小辅助窗口
    /// （实测返回的是一个 373x25 的隐藏输入窗口），截出来是一张几十像素的废图。
    /// 这里改成：枚举该进程所有可见的顶层窗口，取面积最大的那个。
    /// </summary>
    public static IntPtr FindMainWindow(uint targetPid) {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;

        EnumWindows((h, p) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid || !IsWindowVisible(h)) return true;

            RECT r;
            if (!GetWindowRect(h, out r)) return true;

            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);

        return best;
    }
}
'@

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
$aware = [WinShot]::SetProcessDpiAwarenessContext([IntPtr](-4))
if (-not $aware) {
    Write-Warning "无法把本进程提升为 DPI 感知，截图坐标可能对不上窗口位置"
}

Add-Type -AssemblyName System.Drawing

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Error "进程 $ProcessName 没在运行"
    exit 1
}

$hwnd = [WinShot]::FindMainWindow([uint32]$proc.Id)
if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "找不到 $ProcessName 的可见窗口（窗口可能被隐藏到托盘了）"
    exit 1
}

# SW_SHOW = 5：窗口可能被 Hide 到托盘了，先叫出来
[void][WinShot]::ShowWindow($hwnd, 5)
[void][WinShot]::SetForegroundWindow($hwnd)

# 等窗口完成绘制（Mica 材质和卡片图标都是异步上来的）
Start-Sleep -Milliseconds $WaitMs

$rect = New-Object WinShot+RECT
if (-not [WinShot]::GetWindowRect($hwnd, [ref]$rect)) {
    Write-Error "拿不到窗口位置"
    exit 1
}

$width  = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $g.Dispose()
    $bmp.Dispose()
}

$scale = [Math]::Round([WinShot]::GetDpiForWindow($hwnd) / 96 * 100)
Write-Output "已截图 $Out  ($width x $height 物理像素 @ ${scale}% 缩放)"
