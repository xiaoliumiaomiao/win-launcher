<#
    发布 + 安装桌面快捷方式。

    用法：
      pwsh -File tools\install.ps1              # 发布并创建/更新桌面快捷方式
      pwsh -File tools\install.ps1 -SkipPublish # 只重建快捷方式

    产物：publish\WinLauncher.exe —— 单个自包含 exe，不需要装 .NET 运行时。
#>
param(
    [switch]$SkipPublish,
    [string]$ShortcutName = '应用启动器'
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
# 必须用 SpecialFolder.DesktopDirectory，不能硬编码 %USERPROFILE%\Desktop ——
# 开了 OneDrive 桌面同步的话，桌面会被重定向到 OneDrive 目录下。
$desktop  = [Environment]::GetFolderPath('DesktopDirectory')
$linkPath = Join-Path $desktop "$ShortcutName.lnk"

# ⚠️ WScript.Shell 是 ANSI 时代的 COM 组件，写不了含中文的路径 ——
#    在"非 Unicode 程序的语言"设成西欧(1252) 的系统上，中文会变成 "???" 然后 Save 失败。
#    所以先建一个纯 ASCII 名的，属性设好，最后用 Rename-Item（.NET，Unicode 安全）改中文名。
$temporaryLink = Join-Path $desktop ('WinLauncher-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.lnk')

$shell    = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($temporaryLink)
$shortcut.TargetPath       = $exe
$shortcut.WorkingDirectory = $publish
$shortcut.IconLocation     = "$exe,0"
$shortcut.Description      = '桌面应用启动器'
$shortcut.Save()

if (Test-Path -LiteralPath $linkPath) { Remove-Item -LiteralPath $linkPath -Force }
Rename-Item -LiteralPath $temporaryLink -NewName "$ShortcutName.lnk" -Force

Write-Output "桌面快捷方式：$linkPath"
Write-Output ''
Write-Output '接下来手动做两件事，桌面就只剩这一个图标了：'
Write-Output '  1. 把桌面上其它快捷方式删掉或拖进启动器里'
Write-Output '  2. 桌面空白处右键 → 查看 → 取消勾选「显示桌面图标」可以藏掉回收站等系统图标'
Write-Output '     （注意：勾掉后这个启动器图标也会一起藏起来，所以这一步看你自己取舍）'
