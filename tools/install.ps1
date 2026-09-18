<#
    发布 + 安装桌面快捷方式。

    用法：
      pwsh -File tools\install.ps1              # 发布并创建/更新桌面快捷方式
      pwsh -File tools\install.ps1 -SkipPublish # 只重建快捷方式

    产物：publish\WinLauncher.exe —— 单个自包含 exe，不需要装 .NET 运行时。
#>
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\WinLauncher\WinLauncher.csproj'
$publish = Join-Path $root 'publish'
$exe     = Join-Path $publish 'WinLauncher.exe'

if (-not $SkipPublish) {
    Get-Process WinLauncher -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800

    Write-Output '正在发布（自包含单文件，首次会慢一点）…'
    & dotnet publish $project `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none --nologo -v q -o $publish

    if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }
}

if (-not (Test-Path $exe)) { throw "找不到发布产物：$exe" }

$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Output "发布完成：$exe  ($size MB)"

# ── 桌面快捷方式 ──────────────────────────────────────────────
#
# ⚠️ 刻意不用 WScript.Shell（PowerShell 里最省事的做法）。
#    它是 ANSI 时代的 COM 组件：在"非 Unicode 程序的语言"被设成西欧(1252) 的系统上，
#    只要路径里有中文就写不了 —— 中文会变成 "???"，连 Set TargetPath 都会抛
#    "Value does not fall within the expected range"。而这个项目的目录名
#    （win启动工具）和快捷方式名（应用启动器）全是中文，必然踩中。
#
#    程序自己用的是 IShellLinkW，Unicode 原生，所以交给它建。
#    顺带这也让"从 GitHub 下载单文件 exe"的人有办法建快捷方式（设置里有按钮）。
$desktop  = [Environment]::GetFolderPath('DesktopDirectory')
$linkPath = Join-Path $desktop '应用启动器.lnk'

$process = Start-Process -FilePath $exe -ArgumentList '--create-shortcut' -Wait -PassThru

if ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $linkPath)) {
    Write-Output "桌面快捷方式：$linkPath"
}
else {
    Write-Warning "创建桌面快捷方式失败（退出码 $($process.ExitCode)）。"
    Write-Warning "可以在程序里「设置 → 创建桌面快捷方式」手动重试。"
}

Write-Output ''
Write-Output '接下来手动做两件事，桌面就只剩这一个图标了：'
Write-Output '  1. 把桌面上其它快捷方式删掉或拖进启动器里'
Write-Output '  2. 桌面空白处右键 → 查看 → 取消勾选「显示桌面图标」可以藏掉回收站等系统图标'
Write-Output '     （注意：勾掉后这个启动器图标也会一起藏起来，所以这一步看你自己取舍）'
