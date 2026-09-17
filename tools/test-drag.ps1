<#
    验证「拖拽添加」这条链路。

    先用资源管理器做对照（它确定能接受文件拖放，用来证明探针本身是好的），
    再对主程序拖一次，检查文件有没有真的进到配置里。

    前置：主程序必须以**普通权限**运行。如果它是以管理员身份启动的，
    Windows 的 UIPI 会拦下 explorer 发来的拖放消息，这条链路必然失败 —— 脚本会检测并提示。

    用法：pwsh -File tools\test-drag.ps1
#>
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$probe  = Join-Path $root 'tools\DragProbe\bin\Release\net10.0-windows\DragProbe.exe'
$cfg    = Join-Path $env:APPDATA 'AppLauncher\launcher.json'
$script:pass = 0
$script:fail = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Output "  [通过] $name" }
    else     { $script:fail++; Write-Output "  [失败] $name  $detail" }
}

# ── 窗口定位与激活（必须 DPI 感知，否则坐标全错）────────────────
$helper = @'
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr h, uint a, out IntPtr t);
  [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr t, int c, out uint i, uint l, out uint r);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint a, bool i, uint pid);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public delegate bool P(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P cb, IntPtr p);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

  public static bool IsElevated(uint pid) {
    IntPtr proc = OpenProcess(0x1000, false, pid);
    if (proc == IntPtr.Zero) return false;
    IntPtr token;
    if (!OpenProcessToken(proc, 0x0008, out token)) { CloseHandle(proc); return false; }
    uint info, ret;
    bool ok = GetTokenInformation(token, 20, out info, 4, out ret);
    CloseHandle(token); CloseHandle(proc);
    return ok && info != 0;
  }

  public static IntPtr Biggest(uint pid) {
    IntPtr best = IntPtr.Zero; long ba = 0;
    EnumWindows((h,p) => {
      uint x; GetWindowThreadProcessId(h, out x);
      if (x != pid || !IsWindowVisible(h)) return true;
      RECT r; GetWindowRect(h, out r);
      long a = (long)(r.R-r.L)*(r.B-r.T);
      if (a > ba) { ba = a; best = h; }
      return true;
    }, IntPtr.Zero);
    return best;
  }

  public static IntPtr FindExplorer() {
    IntPtr f = IntPtr.Zero;
    EnumWindows((h,p) => {
      if (!IsWindowVisible(h)) return true;
      var c = new StringBuilder(64); GetClassNameW(h, c, 64);
      if (c.ToString() != "CabinetWClass") return true;
      RECT r; GetWindowRect(h, out r);
      if ((r.R-r.L) < 400) return true;
      f = h; return false;
    }, IntPtr.Zero);
    return f;
  }

  public static int[] Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return new int[]{r.L,r.T,r.R,r.B}; }
}
"@
[void][Win]::SetProcessDpiAwarenessContext([IntPtr](-4))
'@

function Get-WindowInfo([string]$mode, [int]$targetPid) {
    # 参数不能叫 $pid —— 那是 PowerShell 的只读内置变量
    $script = $helper + @"

if ('$mode' -eq 'explorer') { `$h = [Win]::FindExplorer() } else { `$h = [Win]::Biggest([uint32]$targetPid) }
if (`$h -eq [IntPtr]::Zero) { 'NONE' } else {
  [void][Win]::SetForegroundWindow(`$h); Start-Sleep -Milliseconds 900
  `$r = [Win]::Rect(`$h); "RECT `$(`$r[0]) `$(`$r[1]) `$(`$r[2]) `$(`$r[3])"
}
if ('$mode' -eq 'app') { "ELEV " + [Win]::IsElevated([uint32]$targetPid) }
"@
    return pwsh -NoProfile -Command $script
}

# ── 准备 ────────────────────────────────────────────────────────
if (-not (Test-Path $probe)) { throw "先构建探针：dotnet build tools\DragProbe\DragProbe.csproj -c Release" }

$p = Get-Process WinLauncher -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { throw "主程序没在运行" }

Write-Output "主程序 PID=$($p.Id)"

$srcDir = Join-Path $env:TEMP ("dragsrc-" + (Get-Date -Format 'HHmmss'))
New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
$txt = Join-Path $srcDir 'probe.txt'
[System.IO.File]::WriteAllText($txt, "drag probe payload")

# ── 对照组：资源管理器 ──────────────────────────────────────────
Write-Output "`n=== 对照组：拖到资源管理器（证明探针本身是好的）==="
$dstDir = Join-Path $env:TEMP ("dragdst-" + (Get-Date -Format 'HHmmss'))
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
Start-Process explorer.exe -ArgumentList "`"$dstDir`"" | Out-Null
Start-Sleep -Seconds 3

$info = Get-WindowInfo 'explorer' 0
$line = ($info | Where-Object { $_ -like 'RECT*' } | Select-Object -Last 1)
if (-not $line) {
    Check "找到资源管理器窗口" $false
} else {
    $q = $line -split '\s+'
    $tx = [int](([int]$q[1] + [int]$q[3]) / 2)
    $ty = [int](([int]$q[2] + [int]$q[4]) / 2)
    & $probe $txt $tx $ty | Out-Null
    Start-Sleep -Seconds 2
    $landed = @(Get-ChildItem $dstDir -ErrorAction SilentlyContinue).Count -gt 0
    Check "探针能把文件拖进资源管理器" $landed "目标点 ($tx,$ty)，接收目录仍然为空"
}

# ── 被测对象：主程序 ────────────────────────────────────────────
Write-Output "`n=== 被测：拖到应用启动器 ==="
$info = Get-WindowInfo 'app' $p.Id
$elev = ($info | Where-Object { $_ -like 'ELEV*' } | Select-Object -Last 1)
$isElevated = $elev -and ($elev -split '\s+')[1] -eq 'True'

Check "主程序以普通权限运行" (-not $isElevated) "它现在是提权的，UIPI 会拦下 explorer 的拖放，必然失败"

$line = ($info | Where-Object { $_ -like 'RECT*' } | Select-Object -Last 1)
if (-not $line) { throw "找不到主程序窗口" }
$q = $line -split '\s+'
$L=[int]$q[1]; $T=[int]$q[2]; $R=[int]$q[3]; $B=[int]$q[4]
$tx = $L + [int](($R-$L) * 0.62)
$ty = $T + [int](($B-$T) * 0.55)

# 记下拖之前的条目数
$before = 0
if (Test-Path $cfg) {
    $d = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
    $before = @($d.Categories | ForEach-Object { $_.Apps }).Count
}

& $probe $txt $tx $ty | Out-Null
Start-Sleep -Seconds 2

$after = 0
$newName = ''
if (Test-Path $cfg) {
    $d = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
    $apps = @($d.Categories | ForEach-Object { $_.Apps })
    $after = $apps.Count
    $newName = ($apps | Where-Object { $_.StableKey -like '*probe.txt*' } | Select-Object -First 1).Name
}

Check "拖拽后条目数 +1" ($after -eq $before + 1) "之前 $before，之后 $after"
Check "新条目是刚拖进去的文件" (-not [string]::IsNullOrEmpty($newName)) "没找到 probe.txt 对应的条目"
if ($newName) { Write-Output "       新条目名称: '$newName'" }

Write-Output "`n==================== 结果 ===================="
Write-Output "通过 $script:pass  失败 $script:fail"
Write-Output "探针日志: $env:TEMP\dragprobe.log"
if ($script:fail -gt 0) { exit 1 }
