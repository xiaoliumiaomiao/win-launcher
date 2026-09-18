using WinLauncher.Models;

namespace WinLauncher.Services;

/// <summary>
/// 把应用归到语义分类。
///
/// 为什么要它：开始菜单的文件夹名是软件厂商给自己起的目录名，不是分类。
/// 照搬的结果是一台普通机器上 32 个"分类"、其中 18 个只有一个应用，
/// 还夹杂 Balena Ltd / CC Switch / Logi 这种厂商名。分类粒度完全由厂商决定，
/// 用户根本没法按用途找东西。
///
/// 这里改成固定的语义分类 + 关键词匹配。分类集是全中文的，也不随装的软件变化。
///
/// 匹配串是「应用名 + 开始菜单文件夹名 + 目标路径」拼起来的 ——
/// 文件夹名虽然不适合直接当分类，但作为信号很强（"腾讯游戏"、"Node.js"）。
///
/// 关键词规则改一个词就可能影响一批应用，改之前先跑
/// <c>tools\preview-categories.ps1</c> 看真实数据上的效果。
/// </summary>
public static class CategoryClassifier
{
    public sealed record Definition(string Id, string Name, int Order, string[] Keywords);

    // 内置分类的 Id 固定。用户给分类改名不会影响分类器识别，
    // 手写的规则也不会因为名字变了而失配。
    public const string Development = "builtin:development";
    public const string Games = "builtin:games";
    public const string Media = "builtin:media";
    public const string Social = "builtin:social";
    public const string Network = "builtin:network";
    public const string Office = "builtin:office";
    public const string Browser = "builtin:browser";
    public const string Hardware = "builtin:hardware";
    public const string WindowsAdmin = "builtin:windows-admin";
    public const string SystemTools = "builtin:system-tools";
    public const string Other = "builtin:other";

    /// <summary>
    /// 分类定义。**顺序即优先级，先匹配到的赢**，所以顺序不能随便调：
    ///   - 开发工具排在系统工具前面：PowerShell / 终端两边都像，但对用这套工具的人
    ///     它们首先是开发工具
    ///   - 游戏排在网络与传输前面：加速器既是游戏工具也是网络工具
    ///   - Windows 管理工具排在系统工具前面：事件查看器/服务这些既像系统工具
    ///     又是明确的管理工具，先让管理工具收走
    /// </summary>
    public static readonly Definition[] Definitions =
    [
        new(Development, "开发工具", 10,
        [
            "visual studio", "vscode", "vs code", "git", "github", "gitlab", "node", "npm", "nvm", "yarn", "pnpm",
            "python", "pip", "conda", "anaconda", "java", "jdk", "jre", "docker", "wsl", "ubuntu", "debian", "linux",
            "finalshell", "xshell", "putty", "mobaxterm", "securecrt", "ssh", "telnet", "jetbrains", "pycharm",
            "intellij", "webstorm", "goland", "clion", "rider", "sublime", "atom", "notepad++", "vim", "emacs",
            "cursor", "postman", "insomnia", "dbeaver", "navicat", "mysql", "redis", "mongodb", "sqlite", "heidisql",
            "sqlserver", "cmake", "mingw", "cygwin", "msys", "balena", "etcher", "rufus", "arduino", "platformio",
            "unity", "unreal", "codebuddy", "cc switch", "ccswitch", "codex switcher",
            "trae", "windsurf", "claude", "copilot",
            "vite", "webpack", "flutter", "android studio", "gcc", "clang", "gradle", "maven", "term",
            "powershell", "cmd", "wireshark", "fiddler", "charles", "toolchain", "developer",
            "命令提示符", "终端", "开发", "编程", "代码", "调试",
        ]),

        new(Games, "游戏", 20,
        [
            "steam", "epic", "ubisoft", "uplay", "origin", "ea app", "battlenet", "blizzard", "rockstar", "gog",
            "wegame", "rungameid", "counter-strike", "pubg", "palworld", "apex", "valorant", "minecraft",
            "vortex", "mod manager", "gaming",
            "游戏", "战网", "对战平台", "完美世界", "浩方", "米哈游", "缺氧", "双人成行", "幻兽帕鲁",
            "我的世界", "原神", "加速器", "雷神", "网易uu", "奇游", "游戏大厅", "模组",
        ]),

        new(Media, "影音", 30,
        [
            "potplayer", "vlc", "mpv", "mpc", "kmplayer", "foobar", "aimp", "itunes", "spotify",
            "handbrake", "obs", "ffmpeg", "audacity", "premiere", "livecaptions",
            "播放器", "player", "media", "music", "网易云", "qq音乐", "酷狗", "酷我", "音乐",
            "剪映", "录屏", "直播伴侣", "格式工厂", "剪辑", "字幕", "实时字幕",
            "爱奇艺", "腾讯视频", "优酷", "bilibili", "哔哩哔哩", "视频", "音频", "影音",
        ]),

        new(Social, "社交通讯", 40,
        [
            "wechat", "qq", "telegram", "dingtalk", "lark", "discord", "skype", "whatsapp", "line",
            "zoom", "mail", "outlook", "thunderbird", "foxmail",
            "微信", "wechat", "钉钉", "飞书", "企业微信", "语音", "腾讯会议", "邮件", "社交", "聊天", "通讯",
        ]),

        new(Network, "网络与传输", 50,
        [
            "clash", "v2ray", "shadowsocks", "trojan", "vpn", "localsend", "ftp", "filezilla", "winscp",
            "teamviewer", "anydesk", "todesk", "sunlogin",
            "download", "idm", "internet download", "thunder", "remote",
            "网盘", "百度网盘", "阿里云盘", "夸克", "迅雷", "下载", "远程", "向日葵", "代理", "抓包",
        ]),

        new(Office, "办公学习", 60,
        [
            "office", "word", "excel", "powerpoint", "wps", "pdf", "acrobat", "onenote", "notion",
            "obsidian", "typora", "markdown", "xmind", "calibre", "foxit", "translate", "kindle",
            "作家", "有道", "词典", "笔记", "思维导图", "幕布", "翻译", "学习", "考试",
            "图书", "阅读", "文档", "办公", "写作", "论文",
        ]),

        new(Browser, "浏览器", 70,
        [
            "chrome", "edge", "firefox", "opera", "brave", "safari", "chromium", "vivaldi",
            "浏览器", "browser",
        ]),

        new(Hardware, "硬件驱动", 80,
        [
            "nvidia", "geforce", "radeon", "realtek", "logitech", "logicool", "ghub", "corsair",
            "razer", "asus", "gigabyte", "afterburner",
            "显卡", "声卡", "网卡", "驱动", "driver", "罗技", "雷蛇", "海盗船", "华硕", "微星", "技嘉",
            "显示器", "键鼠", "手柄", "主板",
        ]),

        new(WindowsAdmin, "Windows 管理工具", 90,
        [
            "event viewer", "services", "computer management", "registry", "disk management",
            "task scheduler", "performance monitor", "resource monitor", "system configuration",
            "system information", "component services", "print management", "security configuration",
            "memory diagnostics", "odbc", "iscsi", "hyper-v", "vmcreate", "recoverydrive",
            "disk cleanup", "dfrgui", "firewall", "gpedit", "administrative tools",
            "事件查看器", "服务", "计算机管理", "注册表", "磁盘管理", "任务计划", "性能监视器",
            "资源监视器", "系统配置", "系统信息", "组件服务", "打印管理", "安全配置", "内存诊断",
            "数据源", "虚拟机", "恢复驱动器", "磁盘清理", "碎片整理", "防火墙", "组策略", "本地安全策略",
        ]),

        new(SystemTools, "系统工具", 100,
        [
            "task manager", "control panel", "file explorer", "everything", "ccleaner", "diskgenius",
            "7-zip", "winrar", "bandizip", "notepad", "calculator", "paint", "snip", "zip", "rar",
            "magnify", "narrator", "system tools", "accessories", "accessibility", "ease of access",
            // 这几个是英文名的 Windows 自带工具。中英各写一份 ——
            // 只写中文的话 "Character Map" / "Steps Recorder" / "VoiceAccess" 全都匹配不上，
            // 实测就是这么掉进「其他」的。
            "character map", "steps recorder", "on-screen keyboard", "voice access", "voiceaccess",
            "任务管理器", "控制面板", "文件资源管理器", "压缩", "截图", "清理", "分区", "备份",
            "计算器", "记事本", "画图", "便签", "放大镜", "讲述人", "屏幕键盘", "语音访问",
            "辅助功能", "字符映射", "步骤记录器", "附件", "系统工具", "运行",
        ]),
    ];

    /// <summary>
    /// 一个应用该同时出现在哪些分类里。
    ///
    /// 只放"确实两边都说得通、且用户明确会想在两处都看到"的。故意保持很短 ——
    /// 多归错一个的代价（卡片出现在你没想到的地方）比省下的点击大。
    /// 其余的交给右键菜单里的「归类 ▸」手动加。
    /// </summary>
    private static readonly (string[] Keywords, string[] ExtraCategoryIds)[] ExtraCategories =
    [
        // 终端类：既是开发工具也是系统工具，两边都放
        (["powershell", "cmd", "term", "wsl", "ubuntu", "debian", "linux", "命令提示符", "终端"],
         [Development, SystemTools]),
    ];

    /// <summary>
    /// 归类。返回分类 Id 列表，第一个是主分类。
    /// </summary>
    /// <param name="name">应用显示名。</param>
    /// <param name="folderName">它所在的开始菜单文件夹名（作为信号，不是结果）。</param>
    /// <param name="targetPath">解析出的真实目标路径。</param>
    public static List<string> Classify(string name, string? folderName, string? targetPath)
    {
        var haystack = $"{name} {folderName} {targetPath}".ToLowerInvariant();

        var result = new List<string> { Other };

        foreach (var definition in Definitions)
        {
            if (!KeywordMatcher.MatchesAny(haystack, definition.Keywords))
                continue;

            result[0] = definition.Id;
            break;
        }

        // 附加分类：主分类是"最像的那个"，但有些东西两边都说得通
        foreach (var (keywords, extras) in ExtraCategories)
        {
            if (!KeywordMatcher.MatchesAny(haystack, keywords))
                continue;

            foreach (var extra in extras)
            {
                if (result[0] != extra && !result.Contains(extra))
                    result.Add(extra);
            }
        }

        return result;
    }

    /// <summary>按定义把内置分类补进数据里。已存在的按 Id 复用，不会重复创建。</summary>
    public static void EnsureBuiltInCategories(LauncherData data)
    {
        foreach (var definition in Definitions)
        {
            var existing = data.Categories.FirstOrDefault(c =>
                string.Equals(c.Id, definition.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // 名字可能被用户改过，不覆盖
                existing.IsBuiltIn = true;
                existing.Order = definition.Order;
                continue;
            }

            data.Categories.Add(new Category
            {
                Id = definition.Id,
                Name = definition.Name,
                Order = definition.Order,
                Source = AppSource.Scan,
                IsBuiltIn = true,
            });
        }

        // 兜底分类也要建出来。
        // 它不在 Definitions 里（没有关键词，是匹配不上时的落点），
        // 但归到它名下的应用需要有一个真实的 Category 对象才能显示出来 ——
        // 少了这一段的后果是：这些应用拿到一个不存在的分类 id，
        // 于是从界面上彻底消失，而且不报任何错。
        var other = data.Categories.FirstOrDefault(c =>
            string.Equals(c.Id, Other, StringComparison.OrdinalIgnoreCase));

        if (other is null)
        {
            data.Categories.Add(new Category
            {
                Id = Other,
                Name = "其他",
                Order = 1000,
                Source = AppSource.Scan,
                IsBuiltIn = true,
            });
        }
        else
        {
            other.Order = 1000;
            other.IsBuiltIn = true;
        }
    }

    /// <summary>这个 Id 是不是内置分类。</summary>
    public static bool IsBuiltInId(string id)
        => Definitions.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
           || string.Equals(id, Other, StringComparison.OrdinalIgnoreCase);
}
