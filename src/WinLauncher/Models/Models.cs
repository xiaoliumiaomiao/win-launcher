using System.Text.Json.Serialization;

namespace WinLauncher.Models;

/// <summary>条目来源：手动拖入、扫描同步、还是内置分类器。</summary>
public enum AppSource
{
    /// <summary>用户拖入或手动添加，扫描同步永远不会碰它。</summary>
    Manual,

    /// <summary>由扫描同步生成，归同步逻辑管理。</summary>
    Scan,
}

/// <summary>一个可启动的应用条目，对应界面上一张卡片。</summary>
public sealed class AppEntry
{
    /// <summary>稳定标识，跨同步不变。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>卡片上显示的名字。</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 实际被启动的东西。三种可能：
    /// 托管目录里的 .lnk 副本（拖入快捷方式时复制过来的）、
    /// 原始 .exe 绝对路径（不复制二进制）、或一个 URL。
    /// </summary>
    public string LaunchPath { get; set; } = "";

    /// <summary>
    /// 解析出的真实目标，用作跨来源去重的稳定键（规范化小写）。
    /// 这个键也是简介/别名/分类归属的持久化锚点。
    /// </summary>
    public string StableKey { get; set; } = "";

    /// <summary>
    /// 附加启动参数。.lnk 自带的参数不需要存这里 ——
    /// 我们启动的是 .lnk 副本本身，shell 会自动应用它内嵌的参数和工作目录。
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>卡片下方那行简介。空字符串表示还没写。</summary>
    public string Description { get; set; } = "";

    /// <summary>用户改过显示名。为 true 时同步不再覆盖 Name。</summary>
    public bool NameCustomized { get; set; }

    public AppSource Source { get; set; } = AppSource.Manual;

    /// <summary>
    /// 扫描来源的标识（对应 <see cref="ScanSource.Id"/>）。手动条目为 null。
    /// 必须和 <see cref="ScanRelativePath"/> 一起用 —— 相对路径只在单个来源内唯一。
    /// </summary>
    public string? ScanSourceId { get; set; }

    /// <summary>扫描来源：相对该来源根的路径（小写）。手动条目为 null。</summary>
    public string? ScanRelativePath { get; set; }

    /// <summary>
    /// ★ 这个应用属于哪些分类。
    ///
    /// 刻意做成"应用记着自己属于谁"而不是"分类装着一堆应用" ——
    /// 后者天然限制了一个应用只能属于一个分类，而像 PowerShell 这种东西
    /// 既该在「开发工具」也该在「系统工具」。做成数组后，
    /// 改归属就只是改一个数组，不用把条目在各个分类之间搬来搬去。
    /// </summary>
    public List<string> CategoryIds { get; set; } = [];

    public int Order { get; set; }

    /// <summary>以管理员身份启动（走 runas verb）。</summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>
    /// 目标找不到的时刻；null 表示正常。
    /// 刻意"只标记不删除"：U 盘没插、网络盘断线都会让目标暂时消失，
    /// 真删的话插回来后简介、分类归属、自定义名就全没了。
    /// </summary>
    public DateTimeOffset? MissingSince { get; set; }

    [JsonIgnore]
    public bool IsMissing => MissingSince is not null;
}

/// <summary>
/// 一层分类，不允许嵌套子分类。
/// 它现在只是一个"标签"，具体的应用在 <see cref="LauncherData.Apps"/> 里，
/// 各自记着自己属于哪些分类。
/// </summary>
public sealed class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>排序用。内置分类用固定的序号，手建分类排在后面。</summary>
    public int Order { get; set; }

    public AppSource Source { get; set; } = AppSource.Manual;

    /// <summary>内置的语义分类。Id 固定，改了名字也不影响分类器识别。</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 手建分类与扫描出来的名字撞车时被复用了，扫描不得回收它。
    /// </summary>
    public bool SharedWithScan { get; set; }
}

/// <summary>
/// 按「目标程序路径」为 key 保存的用户知识。
/// 条目被移除后这里依然保留，等它重新出现时自动回填。
/// </summary>
public sealed class AppKnowledge
{
    public string? Description { get; set; }

    public string? Alias { get; set; }

    /// <summary>
    /// 用户手动改过的分类归属。非 null 时扫描同步不再自动归类这个应用 ——
    /// 否则你费劲拖进「开发工具」的东西，下次同步就被分类器拽回原位了。
    /// </summary>
    public List<string>? CategoryOverride { get; set; }
}

/// <summary>扫描来源的种类。前四种是自动发现的，路径由系统 API 解析。</summary>
public enum ScanSourceKind
{
    /// <summary>所有用户的开始菜单。绝大多数已安装程序都在这。</summary>
    StartMenuCommon,

    /// <summary>当前用户的开始菜单。同一个程序常常两边都有一份，靠目标路径去重。</summary>
    StartMenuUser,

    /// <summary>公共桌面。</summary>
    DesktopCommon,

    /// <summary>当前用户桌面。</summary>
    DesktopUser,

    /// <summary>用户自己指定的目录。</summary>
    CustomFolder,
}

/// <summary>一个扫描来源。</summary>
public sealed class ScanSource
{
    /// <summary>稳定标识。自动来源就是 Kind 的名字；自定义来源加路径哈希。</summary>
    public string Id { get; set; } = "";

    public ScanSourceKind Kind { get; set; }

    /// <summary>只有 <see cref="ScanSourceKind.CustomFolder"/> 用得到。</summary>
    public string? CustomPath { get; set; }

    public bool Enabled { get; set; } = true;

    public string DisplayName { get; set; } = "";
}

/// <summary>扫描配置。</summary>
public sealed class ScanConfig
{
    /// <summary>
    /// 旧版本（v1）只有单一扫描根。仅用于迁移 —— 读到之后会转成一个
    /// <see cref="ScanSourceKind.CustomFolder"/> 来源，然后置空。
    /// </summary>
    public string? RootPath { get; set; }

    public List<ScanSource> Sources { get; set; } = [];

    /// <summary>过滤掉卸载程序、帮助文档、官网链接、许可证这类快捷方式。</summary>
    public bool FilterNoise { get; set; } = true;

    public DateTimeOffset? LastSyncUtc { get; set; }

    /// <summary>第一次自动扫描是否已经跑过。</summary>
    public bool HasAutoScanned { get; set; }
}

/// <summary>全局设置。</summary>
public sealed class AppSettings
{
    /// <summary>全局热键，格式如 "Alt+." 。</summary>
    public string HotKey { get; set; } = "Alt+.";

    /// <summary>关闭按钮是否缩到托盘而不是退出。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>启动一个应用后自动把窗口收起来。</summary>
    public bool HideAfterLaunch { get; set; } = true;

    public double WindowWidth { get; set; } = 1040;

    public double WindowHeight { get; set; } = 680;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    /// <summary>上次选中的分类，下次打开时恢复。</summary>
    public string? LastCategoryId { get; set; }
}

/// <summary>整个 launcher.json 的根对象。</summary>
public sealed class LauncherData
{
    /// <summary>当前数据结构版本。2 → 3 把"分类装应用"改成了"应用记分类"。</summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    public List<Category> Categories { get; set; } = [];

    /// <summary>
    /// 所有应用平铺在这里，归属关系由 <see cref="AppEntry.CategoryIds"/> 表达。
    /// 平铺而不是分散在各个分类里，是因为一个应用可以同时属于多个分类 ——
    /// 分散存的话同一个应用会被序列化多份，加载回来就是多个独立对象，
    /// 改一个（比如写简介）其他几份不会跟着变。
    /// </summary>
    public List<AppEntry> Apps { get; set; } = [];

    public ScanConfig Scan { get; set; } = new();

    public AppSettings Settings { get; set; } = new();

    /// <summary>
    /// key = 规范化后的 StableKey。这是"简介/别名/手动分类不丢"的核心：
    /// 它们不以条目为宿主，所以条目被移除再恢复时能原样回填。
    /// </summary>
    public Dictionary<string, AppKnowledge> Knowledge { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
