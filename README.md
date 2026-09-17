# WinLauncher · 桌面应用启动器

桌面只留一个图标，点开后是自动整理好的分类应用面板。

- **装上就能用** —— 首次启动自动扫描开始菜单和桌面，把已安装的程序收进来并按用途分好类，零配置
- **分类是语义的，不是照搬文件夹** —— 固定 11 个中文分类（开发工具 / 游戏 / 影音 / …），不随装的软件数量膨胀
- **一个应用可以同时属于多个分类** —— PowerShell 既在「开发工具」也在「系统工具」
- **自定义外观** —— 不是 Windows 资源管理器那种文件夹样式，是左侧分类栏 + 右侧卡片网格
- **每张卡片下方有一行简介**，说明这个软件是干什么的
- 支持拖拽添加、挂载目录同步，两种方式并存

技术栈：C# / WPF / **.NET 10**，发布为单个自包含 exe（目标机器不需要装 .NET 运行时）。

---

## 快速开始

```powershell
git clone <仓库地址>
cd win-launcher
pwsh -File tools\install.ps1
```

`install.ps1` 会发布成单文件 exe 并在桌面创建快捷方式。之后双击桌面图标即可。

---

## 日常使用

| 操作 | 怎么做 |
|---|---|
| 打开 | 双击桌面「应用启动器」 |
| 随时呼出 | **Alt + .**（任何界面下都有效，左右 Alt 都行） |
| 重新同步 | `F5` |
| 手动添加 | 把桌面/开始菜单的快捷方式**拖进窗口**，或点左下「＋ 添加应用」 |
| 写简介 | **右键卡片 → 编辑简介**，对话框里有「从程序信息自动填充」 |
| 改分类 | **右键卡片 → 归类** → 勾选/取消分类（一眼看出它当前在哪些分类里） |
| 从列表移除 | 右键卡片 → 从列表中移除（只删列表记录，**不删除任何程序文件**） |
| 聚焦搜索框 | `Ctrl+F`；`Esc` 清空搜索，再按一次收起窗口 |
| 关闭 | 点 ✕ 是**收进系统托盘**（否则热键就没用了）；真正退出走**托盘图标右键 → 退出** |

---

## 分类是怎么做的

### 数据来源

默认扫描四个位置，不需要任何配置：

| 来源 | 路径 |
|---|---|
| 我的开始菜单 | `%APPDATA%\Microsoft\Windows\Start Menu\Programs` |
| 所有用户的开始菜单 | `%ProgramData%\Microsoft\Windows\Start Menu\Programs` |
| 我的桌面 | 含 OneDrive 重定向后的真实桌面 |
| 公共桌面 | `%PUBLIC%\Desktop` |

扫描时**不区分文件夹层级**：文件夹再套多少层都会压平，所以永远不会产生子分类。

### 语义归类

分类**不照搬开始菜单的文件夹名** —— 那些是软件厂商给自己起的目录名，不是分类。
照搬的结果是一台普通机器上 32 个"分类"、其中 18 个只有一个应用，还夹杂
`Balena Ltd` / `CC Switch` / `Logi` 这种厂商名。

改成固定 11 个中文分类，按「应用名 + 文件夹名 + 目标路径」里的关键词匹配：

```
开发工具 · 游戏 · 影音 · 社交通讯 · 网络与传输 · 办公学习
浏览器 · 硬件驱动 · Windows 管理工具 · 系统工具 · 其他
```

实测一台装了两年的开发机：133 个候选 → **92 个应用全部归类**（「其他」为空）、
过滤 31 个噪声、11 个分类。

### 关键词匹配的两种模式

这个细节很关键，用错会大面积误伤：

- **纯拉丁字母数字词 → 词边界匹配。** 子串匹配会出人命：关键词 `msi`（微星）
  会命中 `msinfo32.exe`，把「系统信息」分进硬件驱动；`code` 会命中 `CodeBuddy`。
- **中文词 / 含符号的词 → 子串匹配。** 中文没有词边界概念；`notepad++` 也没法按词匹配。

### 噪声过滤

开始菜单里混着大量不是用来启动程序的东西。实测干净机器上这类占 **23%**（133 个里 31 个）：

- **目标指向 `.html` / `.pdf` / `.txt` / `.chm` 等文档的** —— 这条一个不漏地拦住了
  "Git Release Notes"（指向 `releasenotes.html`）、"Python Manuals"（指向 `doc\html\index.html`）
- 名字里带卸载 / 帮助 / 官网 / 许可证 / 更新日志的（支持复数：`release note` 也能匹配 `Release Notes`）

### 多归属

内置规则只放了一条（保持保守，猜错的代价比省下的点击大）：

> PowerShell / cmd / 终端 / WSL → **开发工具 + 系统工具**

其余靠右键「归类」手动加。手动改过的归属记在知识库里，**重新扫描不会被冲掉**。

---

## 设计上的几个关键决定

**应用记着自己属于哪些分类，而不是分类装着应用。**
后者天然限制了一个应用只能属于一个分类。做成「应用 → 分类 id 列表」之后，
多归属和改归属都只是改一个数组，不用把条目在各个分类之间搬来搬去。

**扫描同步只标记失效，从不删除。**
U 盘没插、网络盘断线、程序被临时移走，都会让条目"消失"。这时它只会被标记成失效
（卡片置灰 + 删除线 + 悬停显示原路径），记录、简介、分类归属原样保留。
放回去、按 F5，一切恢复。存储成本是每项几十字节，不值得冒丢数据的风险。

**简介和分类归属不以条目为宿主。**
它们按「目标程序路径」为 key 存在单独的知识库里。所以删掉快捷方式再放回来、
重命名快捷方式、重命名分类，这些都不会丢。

**拖进来的 .lnk 会被复制一份到数据目录。**
因为需求就是「桌面只留一个图标」——桌面那些快捷方式迟早会被清理掉，
只记原路径的话，清完桌面启动器里就全是死链。复制而不是重建，是为了零损失保留
原快捷方式里的自定义图标、启动参数、工作目录和「以管理员身份运行」标志位。
`.exe` 不复制（不能把几十 MB 的程序拷进数据目录）。

**程序必须保持普通权限运行。**
一旦提权，Windows 的 UIPI 会阻止普通权限的资源管理器向它发送拖放消息，
**从桌面拖文件进来会静默失效**——没有报错、没有异常、没有任何提示。
程序启动时会自己检测，提权了就在顶部显示警告条并给一个「以普通权限重启」按钮。

**拖放链路上埋了日志。**
拖放失败是完全静默的，所以每次 `DragOver` / `Drop` 都会写进 `diagnostics.log`。
拖不进去时先看这个文件。

---

## 数据位置

```
%APPDATA%\AppLauncher\
├─ launcher.json          全部配置（分类、应用、简介知识库、扫描来源）
├─ launcher.json.bak      每次保存自动留的上一版
├─ diagnostics.log        诊断日志（拖放事件、同步统计、分类分布）
├─ IconCache\*.png        提取出来的图标缓存
└─ shortcuts\*.lnk        从桌面拖进来时复制的快捷方式副本
```

备份就是拷这一个文件夹。设置里有「打开数据目录」按钮。

写入走「临时文件 + `File.Replace` 原子替换」，写一半崩溃不会损坏配置。
配置文件故意用不转义中文的 JSON 编码器，可以直接手工编辑。

---

## 开发

```powershell
# 构建并运行
dotnet run --project src\WinLauncher\WinLauncher.csproj

# 扫描同步 + 分类器的场景测试（动了 ScanService / CategoryClassifier 之后必跑）
dotnet build src\WinLauncher\WinLauncher.csproj
pwsh -File tools\test-sync.ps1

# 发布 + 建桌面快捷方式
pwsh -File tools\install.ps1
pwsh -File tools\install.ps1 -SkipPublish    # 只重建快捷方式

# 生成图标（改了 tools\make-icon.ps1 的画法之后跑）
pwsh -File tools\make-icon.ps1

# 开发期截图（自动处理高 DPI 和窗口句柄问题）
pwsh -File tools\screenshot.ps1 -Out shot.png

# 验证手写的 COM 声明没写错（改了 Interop\ShellLink.cs 之后跑）
pwsh -File tools\diag-shelllink.ps1
```

**不要用 `dotnet watch`** —— WPF 的 XAML 热重载基本不生效，`dotnet run` 的几秒重建循环反而更快。

调分类规则时不用开界面：每次同步都会把分类分布写进 `diagnostics.log`，直接看日志。

### 目录结构

```
src\WinLauncher\
├─ Models\Models.cs            数据模型（AppEntry / Category / LauncherData / ScanSource）
├─ Interop\
│  ├─ ShellLink.cs             IShellLinkW / IPersistFile COM 声明 ⚠️ vtable 顺序敏感
│  └─ NativeMethods.cs         DwmSetWindowAttribute / IShellItemImageFactory / RegisterHotKey
├─ Services\
│  ├─ AppPaths.cs              所有磁盘路径的唯一来源
│  ├─ DataStore.cs             launcher.json 原子读写 + 结构迁移
│  ├─ ShortcutResolver.cs      .lnk / .url 解析
│  ├─ EntryFactory.cs          文件 → 条目素材；自排除
│  ├─ KeywordMatcher.cs        关键词匹配（词边界 / 子串两种模式）
│  ├─ CategoryClassifier.cs    ★ 语义分类规则
│  ├─ IconService.cs           图标提取 + 两级缓存（专用 STA 线程）
│  ├─ ProcessLauncher.cs       启动应用 / 失效检测
│  ├─ ScanSourceResolver.cs    扫描来源 → 实际路径；默认来源
│  ├─ ScanService.cs           ★ 扫描对账器（唯一的纯函数模块）
│  ├─ LibraryService.cs        增删改 + 知识库写入
│  ├─ ElevationGuard.cs        提权检测 + 去提权重启
│  ├─ Diagnostics.cs           诊断日志
│  ├─ HotKeyService.cs         RegisterHotKey + 热键文本解析
│  ├─ ThemeManager.cs          深色/浅色 + Mica + 圆角
│  └─ TrayIcon.cs              系统托盘
├─ ViewModels\                 轻量 MVVM，无第三方框架
└─ Views\                      编辑应用 / 文本输入 / 设置 三个对话框

tools\
├─ DragProbe\                  真·OLE 拖拽探针（见下）
├─ diag-shelllink.ps1          COM 声明对照验证
├─ install.ps1                 发布 + 桌面快捷方式
├─ make-icon.ps1               生成多尺寸 app.ico
├─ screenshot.ps1              开发期截图
└─ test-sync.ps1               扫描同步 + 分类器场景测试（29 项）
```

### 关于 DragProbe

`tools\DragProbe` 是一个能发起**真实 OLE 拖放**的小程序。WPF 的拖放走 OLE `IDropTarget`，
没有真的拖拽源就没法触发，键盘和单点鼠标模拟都不行 —— 所以要有这么一个东西。

但它**很脆弱**，用之前必须知道这几个坑（都是实际踩过的）：

1. **必须让 Dispatcher 真的跑起来**（`app.Run()`）。只 `Show()` 窗口但不跑消息循环，
   窗口不处理输入消息、拿不到鼠标捕获，`DoDragDrop` 会**立刻**返回 `DROPEFFECT_NONE`。
   最坑的是现象看着像"鼠标没按下去"，但 `GetAsyncKeyState` 查出来按键状态是好的。
2. **拖拽源窗口必须在屏幕内**。放到 `(-3000,-3000)` 会让拖拽循环卡死永不返回。
3. **收尾要用 `DispatcherTimer` 驱动**，不能用 sleep 完再 `mouse_event` 的后台线程 —— 时序对不上。
4. **拖放目标窗口必须真的在最上面**。目标点被别的窗口盖住时，拖放会被那个窗口接走，
   结果是"看起来拖成功了但被测程序什么都没收到"，极具误导性。探针加了 `DescribeWindowAt()`
   做校验，日志里会写"✓ 就是目标窗口"还是"✗ 不是"。
5. **拖拽源和目标的完整性级别必须一致**，否则会失败。所以探针要通过 `.cmd` + `explorer.exe`
   以普通权限启动，被测程序也必须是普通权限。

**如果探针行为诡异，先别怀疑被测程序** —— 先看它日志里第 4 条那个校验是不是 ✗。

---

## 踩过的坑

改代码前扫一眼，都是实际花时间查出来的。

| 坑 | 症状 | 处置 |
|---|---|---|
| `IShellLinkW` 声明里加了 `IPersist::GetClassID` | GetPath 返回 2-4 个乱码字符、Resolve 返回 E_INVALIDARG，**而且 GetPath 还返回 S_OK**，完全不报错 | IShellLinkW 继承的是 IUnknown，第一个方法就是 GetPath。改完跑 `tools\diag-shelllink.ps1` 对照 |
| 关键词用子串匹配 | `msi` 命中 `msinfo32.exe`，把「系统信息」分进硬件驱动 | 纯拉丁词必须词边界匹配（`KeywordMatcher`） |
| 关键词只写中文 | `Character Map` / `Steps Recorder` / `VoiceAccess` 全掉进「其他」 | 英文名的系统工具要中英各写一份 |
| 噪声过滤不支持复数 | `Release Notes`、`Manuals` 因为词边界卡在 "s" 上漏网 | `KeywordMatcher.Matches(..., allowPlural: true)` |
| 靠名字猜是不是文档 | 很难捞干净 | 看**目标后缀**：`.html`/`.pdf`/`.txt` 的就不是应用 |
| 同步时按目标路径单值索引 | 同一程序的多个快捷方式互相抢索引，后面的被误判失效 | 目标路径映射到**列表**，且先按相对路径匹配 |
| 去重时按"所有已有条目"预占目标键 | 已失效的孤儿条目一直占着键，新出现的同名程序永远建不出来 | 只让**本轮匹配上了的**条目占键 |
| 兜底分类没建出来 | 归到「其他」的应用拿到一个不存在的分类 id，**从界面上彻底消失且不报错** | `EnsureBuiltInCategories` 里也要建「其他」 |
| 提权运行 | 拖放彻底静默失效，无报错无日志 | 保持 asInvoker；`ElevationGuard` 检测并提示 |
| 在 `Loaded` 阶段调 `MessageBox.Show` | 对话框不显示，程序看似什么都没做 | 改用行内提示条 |
| `DragOver` 不设 `e.Handled = true` | 能拖到窗口上但松手没反应 | 必设，WPF 会把 `e.Effects` 重置成 None |
| 窗口用 `AllowsTransparency="True"` | 没有 Mica、没有圆角、没有 Snap，文字发虚 | 用 `WindowChrome`；窗口 `Background="{x:Null}"` |
| 忘了 `Freeze()` 就 `DeleteObject(hbm)` | 图标空白或访问违例 | `CreateBitmapSourceFromHBitmap` 之后先 `Freeze()` 再释放 HBITMAP |
| `BitmapImage` 没设 `CacheOption.OnLoad` | PNG 文件句柄不释放，重建缓存时删不掉 | 必设 |
| `Process.MainWindowHandle` 找窗口 | 对 WPF 程序会返回内部的 373×25 辅助窗口 | 枚举顶层窗口取面积最大的 |
| 用 `GetClassNameW` / `GetWindowTextW` 不写 `CharSet.Unicode` | 默认按 ANSI 编组，连续读到第一个字符就截断，类名只剩 1 个字母 | 两个 DllImport 都要 `CharSet = CharSet.Unicode` |
| 在 PowerShell 里量窗口坐标 | 进程创建过窗口后 `SetProcessDpiAwarenessContext` 会失败，物理/逻辑坐标混用 | 用 `tools\screenshot.ps1`，它在全新进程里先设 DPI 感知再量 |
| PowerShell 里 `if ($array.SomeProp)` | 对 `Object[]` 访问不存在的属性返回的是"每个元素取一次"的 `$null` 数组，非空数组是 truthy，会走错分支 | 显式判断类型，别靠真假值 |
| PowerShell 函数里用 `Write-Output` 调试 | 输出会被当成函数返回值拼进结果里 | 函数内调试用 `Write-Host` |
| JSON 用默认编码器 | 中文简介变成 `\uXXXX`，配置文件没法手工编辑 | `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` |
| 硬编码 `%USERPROFILE%\Desktop` | 开了 OneDrive 桌面同步就找不到桌面 | 用 `SpecialFolder.DesktopDirectory` |
| `Directory.GetFiles(dir, "*.exe")` | Win32 通配符有 8.3 短名遗留行为（`*.htm` 匹配到 `.html`） | 枚举 `"*"` 再用后缀 HashSet 过滤 |
| `PublishTrimmed` | WPF 靠反射加载 XAML 类型，裁剪后运行时崩溃 | 绝不开启 |
| 单文件发布下用 `Assembly.Location` | 返回空字符串 | 用 `Environment.ProcessPath` |
| XML 注释里写 `--` | `MSB4025: An XML comment cannot contain '--'` | csproj 和 XAML 注释里别写连续两个减号 |
| WPF 项目的隐式 using 不含 `System.IO` | `Path` / `File` 全都找不到 | csproj 里显式 `<Using Include="System.IO" />` |
| 同时开 `UseWPF` 和 `UseWindowsForms` | `System.Windows.Forms` / `System.Drawing` 全局 using 与 WPF 类型全面重名 | csproj 里 `<Using Remove="..." />` 去掉它们 |

---

## 许可证

[MIT](LICENSE)
