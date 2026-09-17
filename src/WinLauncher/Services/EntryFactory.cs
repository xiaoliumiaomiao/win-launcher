using WinLauncher.Models;

namespace WinLauncher.Services;

/// <summary>从磁盘文件解析出的条目素材。</summary>
public sealed record EntrySeed(string LaunchPath, string StableKey, string DisplayName);

/// <summary>把拖进来 / 扫描到的文件变成 <see cref="AppEntry"/>。</summary>
public static class EntryFactory
{
    /// <summary>认识的文件类型。</summary>
    public static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".exe", ".bat", ".cmd", ".ps1" };

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// 解析一个文件。
    /// </summary>
    /// <param name="path">源文件路径。</param>
    /// <param name="copyShortcutToManagedDir">
    /// 是否把 .lnk/.url 复制到托管目录。
    /// 手动拖入时必须复制 —— 需求是"桌面只留一个图标"，桌面那些 .lnk 迟早会被删掉，
    /// 引用原路径的话清完桌面启动器里就全是死链了。
    /// 扫描来源则保持就地引用（它本来就在用户自己维护的目录里，复制反而会和同步逻辑打架）。
    /// </param>
    public static EntrySeed? Resolve(string path, bool copyShortcutToManagedDir)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;

        var ext = Path.GetExtension(path);

        if (Directory.Exists(path))
        {
            // 拖进来的是文件夹：不支持，交给调用方提示
            return null;
        }

        return ext.ToLowerInvariant() switch
        {
            ".lnk" => ResolveShortcut(path, copyShortcutToManagedDir),
            ".url" => ResolveUrl(path, copyShortcutToManagedDir),
            _ => new EntrySeed(
                path,
                ShortcutResolver.NormalizeStableKey(path),
                Path.GetFileNameWithoutExtension(path)),
        };
    }

    private static EntrySeed ResolveShortcut(string lnkPath, bool copy)
    {
        var resolved = ShortcutResolver.Resolve(lnkPath);
        var launchPath = copy ? CopyToManagedDirectory(lnkPath) : lnkPath;

        // 显示名优先用快捷方式自己的文件名 —— 那是用户亲手起的名字，
        // 比目标 exe 的文件名（notepad、cmd）有意义得多。
        var displayName = CleanShortcutSuffix(Path.GetFileNameWithoutExtension(lnkPath));

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = resolved.Resolved
                ? Path.GetFileNameWithoutExtension(resolved.Target)
                : "未命名";
        }

        // UWP 商店应用等特殊快捷方式的 TargetPath 是空的。
        // 这时仍然可以靠 shell 启动 —— 存 .lnk 本身，双击行为一致。
        var stableKey = resolved.Resolved
            ? ShortcutResolver.NormalizeStableKey(resolved.Target)
            : ShortcutResolver.NormalizeStableKey(lnkPath);

        return new EntrySeed(launchPath, stableKey, displayName);
    }

    /// <summary>
    /// 中文版 Windows 给新建的快捷方式自动加 " - 快捷方式" 后缀，
    /// 英文版是 " - Shortcut"。显示名里带着这个尾巴很难看。
    /// </summary>
    private static string CleanShortcutSuffix(string name)
    {
        string[] suffixes = [" - 快捷方式", " 的快捷方式", " - Shortcut", " - Link"];

        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length].TrimEnd();
        }

        return name.Trim();
    }

    /// <summary>
    /// 给新条目一个简介起点：先查知识库（用户以前写过就用用户的），
    /// 查不到再去读程序自带的版本描述信息。用户可以随时改。
    /// </summary>
    public static string SeedDescription(LauncherData data, string stableKey)
    {
        if (data.Knowledge.TryGetValue(stableKey, out var knowledge)
            && !string.IsNullOrWhiteSpace(knowledge.Description))
        {
            return knowledge.Description;
        }

        var suggested = SuggestDescription(stableKey);
        if (string.IsNullOrWhiteSpace(suggested))
            return "";

        // 版本描述有时又长又啰嗦，卡片上只放得下一行
        return suggested.Length > 40 ? suggested[..40] : suggested;
    }

    private static EntrySeed ResolveUrl(string urlPath, bool copy)
    {
        var url = ShortcutResolver.ReadUrlFile(urlPath);
        var launchPath = copy ? CopyToManagedDirectory(urlPath) : urlPath;

        // 显示名优先用文件名 —— 网站快捷方式和 Steam 游戏快捷方式的文件名
        // 通常就是网站名/游戏名，是最有信息量的。
        //
        // ⚠️ 千万别拿 new Uri(url).Host 去覆盖它：steam://rungameid/730 的
        //    Host 是 "rungameid"，会让五个不同的游戏全变成同名的 "rungameid"。
        var displayName = CleanShortcutSuffix(Path.GetFileNameWithoutExtension(urlPath));

        if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(url))
        {
            // 只有文件名实在没意义时才退回用域名，而且只对 http(s) 有意义
            try
            {
                var parsed = new Uri(url);
                if (parsed.Scheme is "http" or "https")
                {
                    var host = parsed.Host;
                    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                        host = host[4..];

                    if (!string.IsNullOrWhiteSpace(host))
                        displayName = host;
                }
            }
            catch
            {
                // URL 不合法就用空名，交给调用方兜底
            }
        }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "未命名链接";

        return new EntrySeed(
            launchPath,
            ShortcutResolver.NormalizeStableKey(url ?? urlPath),
            displayName);
    }

    private static string CopyToManagedDirectory(string sourcePath)
    {
        AppPaths.EnsureCreated();
        var destination = Path.Combine(
            AppPaths.ShortcutsDir,
            Guid.NewGuid().ToString("N") + Path.GetExtension(sourcePath));

        // 用 File.Copy 而不是"解析后重建一个 .lnk"：
        // 原文件里内嵌的自定义图标、启动参数、工作目录、
        //「以管理员身份运行」标志位全部原样保留，信息零损失。
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    /// <summary>
    /// 这个条目现在还能不能用。失效的卡片会置灰，而不是点了没反应。
    ///
    /// 两个条件都必须满足：
    ///   1. 启动载体本身还在（.lnk 副本 / exe / .url 文件没被删）
    ///   2. 载体指向的目标还在（程序没被卸载或搬到别处）
    /// 只看第 2 条会漏掉"扫描目录里的文件被删了、但目标程序还装着"的情况；
    /// 只看第 1 条则会漏掉"快捷方式还在、程序已经卸载"的情况。
    /// </summary>
    public static bool IsTargetAlive(AppEntry entry)
    {
        if (!Exists(entry.LaunchPath))
            return false;

        if (!string.IsNullOrWhiteSpace(entry.StableKey)
            && !string.Equals(entry.StableKey, entry.LaunchPath, StringComparison.OrdinalIgnoreCase)
            && !Exists(entry.StableKey))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 这个目标是不是启动器自己。
    /// 自动扫描会把桌面/开始菜单里本程序自己的快捷方式也收进来，
    /// 界面上出现一张"点一下又弹出自己"的卡片很蠢，直接排除。
    /// </summary>
    public static bool IsSelf(string stableKey)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || string.IsNullOrEmpty(stableKey))
            return false;

        if (string.Equals(
                ShortcutResolver.NormalizeStableKey(self),
                stableKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 开发调试时跑的是 bin\Debug 下的 exe，而桌面快捷方式指向的是发布版，
        // 全路径对不上，但它们显然是同一个程序。所以再比一次文件名。
        var selfName = Path.GetFileNameWithoutExtension(self);
        var targetName = Path.GetFileNameWithoutExtension(stableKey);

        return !string.IsNullOrEmpty(selfName)
               && string.Equals(selfName, targetName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // URL / shell: 协议没有对应的文件可言，一律视为有效
        if (path.Contains("://", StringComparison.Ordinal)
            || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return true;

        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>
    /// 从可执行文件自带的版本信息里取一句描述，作为简介的自动填充建议。
    /// 取不到就返回 null，由用户自己写。
    /// </summary>
    public static string? SuggestDescription(string targetPath)
    {
        try
        {
            var ext = Path.GetExtension(targetPath);
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!File.Exists(targetPath))
                return null;

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(targetPath);
            var text = info.FileDescription;
            if (string.IsNullOrWhiteSpace(text))
                text = info.ProductName;

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }
}
