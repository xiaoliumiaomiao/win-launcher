<#
    扫描同步 + 分类器的场景测试。

    这是全项目唯一会"静默毁数据"的地方，动完 ScanService / CategoryClassifier
    / LibraryService 之后必须跑一遍。

    测试用一份隔离的配置：只挂一个自定义目录来源，不碰用户的真实开始菜单，
    所以跑多少次都不会影响正常使用。

    用法：pwsh -File tools\test-sync.ps1
#>
$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$exe     = Join-Path $root 'src\WinLauncher\bin\Debug\net10.0-windows\WinLauncher.exe'
$dataDir = Join-Path $env:APPDATA 'AppLauncher'
$cfg     = Join-Path $dataDir 'launcher.json'

# 每次用一个新的临时目录。固定目录名会被上一轮遗留的句柄锁住
# （资源管理器、杀软扫描都可能持有），导致删除失败。
$fixture = Join-Path $env:TEMP ("launcher-test-" + (Get-Date -Format 'HHmmss'))
$vendorDir = Join-Path $fixture '某厂商'          # 文件夹名不含任何分类关键词，避免干扰分类器
$notepadLnk = Join-Path $vendorDir '记事本.lnk'
$renameLnk  = Join-Path $vendorDir '写字板改名后.lnk'

# 内置分类 Id，和 CategoryClassifier.cs 里的常量一致
$DEV   = 'builtin:development'
$SYS   = 'builtin:system-tools'
$OTHER = 'builtin:other'

$script:pass = 0
$script:fail = 0

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Output "  [通过] $name" }
    else     { $script:fail++; Write-Output "  [失败] $name  $detail" }
}

function Stop-App {
    Get-Process WinLauncher -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

function Invoke-Sync {
    Start-Process -FilePath $exe | Out-Null
    Start-Sleep -Seconds 4
    Stop-App
    return (Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Find-Entry($data, [string]$name) {
    @($data.Apps | Where-Object { $_.Name -eq $name })
}

function CategoriesOf($data, [string]$name) {
    $entry = Find-Entry $data $name
    if ($entry.Count -eq 0) { return @() }
    return @($entry[0].CategoryIds)
}

function CategoryName($data, [string]$id) {
    $c = @($data.Categories | Where-Object { $_.Id -eq $id })
    if ($c.Count -eq 0) { return "(不存在)" }
    return $c[0].Name
}

function New-TestLnk([string]$path, [string]$target) {
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($path)
    $s.TargetPath = $target
    $s.Save()
}

# ==================== 准备 ====================
Stop-App
Write-Output "准备测试夹具: $fixture"
New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null

New-TestLnk $notepadLnk                                "$env:WINDIR\System32\notepad.exe"
New-TestLnk (Join-Path $vendorDir '命令提示符.lnk')      "$env:WINDIR\System32\cmd.exe"
New-TestLnk (Join-Path $vendorDir 'Git Bash.lnk')       "$env:ProgramFiles\Git\git-bash.exe"
New-TestLnk (Join-Path $fixture '任务管理器.lnk')        "$env:WINDIR\System32\Taskmgr.exe"
# 噪声：目标是个 html 文档 —— 应该被"目标是文档"这条规则过滤掉
New-TestLnk (Join-Path $vendorDir 'Git Release Notes.lnk') "$env:ProgramFiles\Git\ReleaseNotes.html"
# 噪声：名字里带"卸载"
New-TestLnk (Join-Path $vendorDir 'Uninstall 某软件.lnk')  "$env:WINDIR\System32\notepad.exe"
# 深层目录里的应用，应该被压平到同一个来源（不再按文件夹分类）
New-Item -ItemType Directory -Force -Path (Join-Path $vendorDir '子目录') | Out-Null
New-TestLnk (Join-Path $vendorDir '子目录\计算器.lnk')     "$env:WINDIR\System32\calc.exe"

if (Test-Path $cfg) { Remove-Item -LiteralPath $cfg -Force }
$seed = [ordered]@{
    Version = 3
    Categories = @()
    Apps = @()
    Scan = [ordered]@{
        Sources = @([ordered]@{
            Id = 'CustomFolder:TESTFIXTURE'
            Kind = 4                       # CustomFolder
            CustomPath = $fixture
            Enabled = $true
            DisplayName = '测试夹具'
        })
        FilterNoise = $true
        HasAutoScanned = $true             # 关掉首次扫描的行内提示
    }
    Settings = [ordered]@{ HotKey='Alt+.'; HideAfterLaunch=$true; WindowWidth=1040; WindowHeight=680 }
    Knowledge = @{}
}
[System.IO.File]::WriteAllText($cfg, ($seed | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))

# ==================== 场景 1：首次同步 + 语义分类 ====================
Write-Output "`n=== 场景 1: 首次同步与语义分类 ==="
$d = Invoke-Sync

Check "应用被摊平到顶层 Apps（不再装在分类里）" (@($d.Apps).Count -gt 0) "Apps 条数 $(@($d.Apps).Count)"
Check "分类是按语义建的，不是文件夹名" (
    (@($d.Categories | Where-Object { $_.Name -eq '某厂商' }).Count -eq 0)) "不该出现「某厂商」这个分类"
Check "内置分类已建立" (@($d.Categories | Where-Object { $_.Id -eq $DEV }).Count -eq 1)
Check "兜底的「其他」分类也存在" (@($d.Categories | Where-Object { $_.Id -eq $OTHER }).Count -eq 1) `
    "少了它，归到「其他」的应用在界面上会彻底看不见"

Check "记事本 → 系统工具" ((CategoriesOf $d '记事本') -contains $SYS) "实际: $((CategoriesOf $d '记事本') -join ',')"

$cmdCats = CategoriesOf $d '命令提示符'
Check "命令提示符 → 开发工具（主分类）" ($cmdCats.Count -ge 1 -and $cmdCats[0] -eq $DEV) "实际: $($cmdCats -join ',')"
Check "命令提示符 → 同时也属于系统工具（多归属）" ($cmdCats -contains $SYS) "实际: $($cmdCats -join ',')"

Check "Git Bash → 开发工具" ((CategoriesOf $d 'Git Bash') -contains $DEV) "实际: $((CategoriesOf $d 'Git Bash') -join ',')"
Check "任务管理器 → 系统工具" ((CategoriesOf $d '任务管理器') -contains $SYS) "实际: $((CategoriesOf $d '任务管理器') -join ',')"
Check "深层子目录里的应用也被收录（压平到同一来源）" ((Find-Entry $d '计算器').Count -eq 1)

Check "HTML 文档类目标被过滤" ((Find-Entry $d 'Git Release Notes').Count -eq 0)
Check "名字带卸载的被过滤" ((Find-Entry $d 'Uninstall 某软件').Count -eq 0)

Check "应用总数 = 5" (@($d.Apps).Count -eq 5) "实际 $(@($d.Apps).Count): $((@($d.Apps) | ForEach-Object { $_.Name }) -join ',')"

# ==================== 场景 2：手写简介后删掉 .lnk ====================
Write-Output "`n=== 场景 2: 删掉 .lnk 后同步（只标记不删除）==="
$d = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$entry = Find-Entry $d '记事本'
$entry[0].Description = '纯文本编辑器（手写）'
$d.Knowledge | Add-Member -NotePropertyName $entry[0].StableKey `
    -NotePropertyValue ([pscustomobject]@{ Description = '纯文本编辑器（手写）'; Alias = $null; CategoryOverride = $null }) -Force
[System.IO.File]::WriteAllText($cfg, ($d | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))

Remove-Item -LiteralPath $notepadLnk -Force
$d = Invoke-Sync
$gone = Find-Entry $d '记事本'
Check "条目没有被删除（只标记）" (@($gone).Count -eq 1) "实际 $(@($gone).Count) 条"
Check "已标记 MissingSince" ($null -ne $gone[0].MissingSince)
Check "简介保留" ($gone[0].Description -eq '纯文本编辑器（手写）') "实际 '$($gone[0].Description)'"
Check "分类归属保留" ((@($gone[0].CategoryIds) -contains $SYS)) "实际: $(@($gone[0].CategoryIds) -join ',')"

# ==================== 场景 3：把 .lnk 放回去 ====================
Write-Output "`n=== 场景 3: 恢复 .lnk 后同步 ==="
New-TestLnk $notepadLnk "$env:WINDIR\System32\notepad.exe"
$d = Invoke-Sync
$back = Find-Entry $d '记事本'
Check "条目仍然是 1 条（没重复）" (@($back).Count -eq 1) "实际 $(@($back).Count) 条"
Check "MissingSince 已清除" ($null -eq $back[0].MissingSince)
Check "简介原样回来" ($back[0].Description -eq '纯文本编辑器（手写）')

# ==================== 场景 4：重命名 .lnk ====================
Write-Output "`n=== 场景 4: 重命名 .lnk 后同步 ==="
Move-Item -LiteralPath $notepadLnk -Destination $renameLnk -Force
$d = Invoke-Sync
Check "重命名后认出来了" ((Find-Entry $d '写字板改名后').Count -eq 1)
Check "简介跟着保留" ((Find-Entry $d '写字板改名后')[0].Description -eq '纯文本编辑器（手写）')
Check "没有留下失效的旧条目" ((Find-Entry $d '记事本').Count -eq 0)
Check "应用总数仍是 5" (@($d.Apps).Count -eq 5)

# ==================== 场景 5：手动改归属不被同步冲掉 ====================
Write-Output "`n=== 场景 5: 手动改过的分类归属不会被重新同步冲掉 ==="
$d = Get-Content $cfg -Raw -Encoding UTF8 | ConvertFrom-Json
$git = Find-Entry $d 'Git Bash'
$git[0].CategoryIds = @($DEV, $OTHER)       # 手动加一个「其他」
$k = $d.Knowledge.PSObject.Properties[$git[0].StableKey]
if ($null -eq $k) {
    $d.Knowledge | Add-Member -NotePropertyName $git[0].StableKey `
        -NotePropertyValue ([pscustomobject]@{ Description = $null; Alias = $null; CategoryOverride = @($DEV, $OTHER) }) -Force
} else {
    $k.Value.CategoryOverride = @($DEV, $OTHER)
}
[System.IO.File]::WriteAllText($cfg, ($d | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))

$d = Invoke-Sync
$gitCats = CategoriesOf $d 'Git Bash'
Check "手动加的「其他」还在" ($gitCats -contains $OTHER) "实际: $($gitCats -join ',')"
Check "分类器没有把它拽回原样" ($gitCats.Count -eq 2) "实际: $($gitCats -join ',')"

# ==================== 场景 6：多归属应用在每个分类里都能看到 ====================
Write-Output "`n=== 场景 6: 多归属 ==="
$devCount = @($d.Apps | Where-Object { @($_.CategoryIds) -contains $DEV }).Count
$sysCount = @($d.Apps | Where-Object { @($_.CategoryIds) -contains $SYS }).Count
$bothCount = @($d.Apps | Where-Object { (@($_.CategoryIds) -contains $DEV) -and (@($_.CategoryIds) -contains $SYS) }).Count
Check "开发工具里能看到命令提示符" ($devCount -ge 2) "开发工具 $devCount 个"
Check "系统工具里也能看到同一个命令提示符" ($sysCount -ge 3) "系统工具 $sysCount 个"
Check "确实存在同时属于两个分类的应用" ($bothCount -ge 1) "多归属 $bothCount 个"

# ==================== 收尾 ====================
Stop-App
Write-Output "`n==================== 结果 ===================="
Write-Output "通过 $script:pass  失败 $script:fail"
if ($script:fail -gt 0) { exit 1 }
