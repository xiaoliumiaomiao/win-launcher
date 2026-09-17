using WinLauncher.Models;

namespace WinLauncher.Services;

/// <summary>扫描到的一个候选文件。</summary>
/// <param name="SourceId">来自哪个来源。相对路径只在单个来源内唯一，必须带上它才能定位。</param>
/// <param name="FolderName">它所在的开始菜单文件夹名。不是分类结果，只是分类器的一个信号。</param>
public sealed record ScanItem(string SourceId, string FolderName, string FullPath, string RelativePath);

/// <summary>一次枚举的结果。</summary>
public sealed record ScanBatch(IReadOnlyList<ScanItem> Items);

/// <summary>一次同步的统计结果。</summary>
public sealed record ScanOutcome(int Added, int Updated, int Missing, int Alive, int FilteredNoise);

/// <summary>
/// 扫描同步。
///
/// 设计上刻意做成「输入全量磁盘状态 → 输出全量新状态」的纯函数，
/// 而不是消费增量事件。将来若要加 FileSystemWatcher 或"回到前台时同步"，
/// 只需防抖后重跑一次全量对账，这里的逻辑一行都不用改。
/// </summary>
public static class ScanService
{
    /// <summary>直接放在来源根下（不在任何子文件夹里）的文件，没有文件夹名可言。</summary>
    public const string NoFolderName = "";

    /// <summary>分类文件夹内部最多再往下钻几层。两级嵌套很常见（腾讯软件\QQ\QQ.lnk）。</summary>
    private const int MaxDepthInsideCategory = 3;

    /// <summary>
    /// 噪声快捷方式的特征词。
    ///
    /// 开始菜单里混着大量"不是用来启动程序"的快捷方式 —— 卸载程序、帮助文档、
    /// 官网链接、许可证、更新日志。实测干净机器上这类能占 20%。
    /// </summary>
    private static readonly string[] NoiseMarkers =
    [
        "uninstall", "remove app", "help", "readme", "documentation",
        "release note", "changelog", "website", "homepage", "home page",
        "license", "faq", "tutorial", "support", "manual",
        "卸载", "帮助", "说明", "文档", "更新日志", "官网", "网站", "许可", "支持", "用户手册",
    ];

    /// <summary>
    /// 目标指向这些后缀的快捷方式不是应用，是文档。
    ///
    /// 这一条比堆关键词管用得多：实测 "Git Release Notes" 指向 releasenotes.html、
    /// "Python Manuals" 指向 doc\html\index.html，靠名字里的关键词很难捞干净，
    /// 但看目标后缀一个不漏。
    /// </summary>
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".txt", ".pdf", ".chm", ".md", ".rtf", ".doc", ".docx", ".log",
    };

    private static readonly EnumerationOptions TopLevelOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.System,
    };

    private static readonly EnumerationOptions NestedOptions = new()
    {
        RecurseSubdirectories = true,
        MaxRecursionDepth = MaxDepthInsideCategory,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.System,
    };

    /// <summary>枚举所有启用来源的磁盘状态。只做粗筛，细筛（噪声）在对账时做。</summary>
    public static ScanBatch Enumerate(IEnumerable<ScanSource> sources)
    {
        var items = new List<ScanItem>();

        foreach (var source in sources)
        {
            if (!source.Enabled)
                continue;

            var root = ScanSourceResolver.ResolvePath(source);
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            try
            {
                // ★ 顺序很重要：先收有分类的，再收根目录下散落的。
                //   因为最后会按目标路径去重、先到先得 —— 如果先收散落的，
                //   一个正经分类文件夹里的应用会被根目录下的重复快捷方式挤掉。
                foreach (var directory in Directory.EnumerateDirectories(root, "*", TopLevelOptions))
                {
                    var folderName = Path.GetFileName(directory);
                    if (string.IsNullOrWhiteSpace(folderName))
                        continue;

                    foreach (var file in Directory.EnumerateFiles(directory, "*", NestedOptions))
                        AddIfCandidate(items, source, folderName, root, file);
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", TopLevelOptions))
                    AddIfCandidate(items, source, NoFolderName, root, file);
            }
            catch
            {
                // 整个来源读不了（U 盘拔了、网络盘断了）就跳过，
                // 对账逻辑会把已有条目标记为失效而不是删除，数据不丢。
            }
        }

        return new ScanBatch(items);
    }

    private static void AddIfCandidate(
        List<ScanItem> items, ScanSource source, string folderName, string root, string file)
    {
        var name = Path.GetFileName(file);

        if (name.StartsWith("~$", StringComparison.Ordinal)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            return;

        if (!EntryFactory.IsSupported(file))
            return;

        items.Add(new ScanItem(source.Id, folderName, file, MakeRelative(root, file)));
    }

    /// <summary>对账。返回本次的增删统计。</summary>
    public static ScanOutcome Apply(LauncherData data, IReadOnlyList<ScanItem> items)
    {
        var now = DateTimeOffset.Now;
        var added = 0;
        var updated = 0;
        var missing = 0;
        var filtered = 0;

        CategoryClassifier.EnsureBuiltInCategories(data);

        // ---------- 1. 索引现有的扫描条目 ----------
        var byStableKey = new Dictionary<string, List<AppEntry>>(StringComparer.OrdinalIgnoreCase);
        var byLocation = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in data.Apps)
        {
            if (app.Source != AppSource.Scan)
                continue;

            if (!string.IsNullOrEmpty(app.StableKey))
            {
                if (!byStableKey.TryGetValue(app.StableKey, out var list))
                    byStableKey[app.StableKey] = list = [];

                list.Add(app);
            }

            var location = LocationKey(app.ScanSourceId, app.ScanRelativePath);
            if (location.Length > 0)
                byLocation.TryAdd(location, app);
        }

        // ---------- 2. 解析 + 过滤噪声 ----------
        var resolved = new List<(ScanItem Item, EntrySeed Seed)>();
        foreach (var item in items)
        {
            var seed = EntryFactory.Resolve(item.FullPath, copyShortcutToManagedDir: false);
            if (seed is null)
                continue;

            // 扫描会把本程序自己的快捷方式也收进来，
            // 界面上出现一张"点一下又弹出自己"的卡片很蠢
            if (EntryFactory.IsSelf(seed.StableKey))
                continue;

            if (data.Scan.FilterNoise && IsNoise(Path.GetFileNameWithoutExtension(item.FullPath), seed.StableKey))
            {
                filtered++;
                continue;
            }

            resolved.Add((item, seed));
        }

        var assigned = new AppEntry?[resolved.Count];
        var isNewEntry = new bool[resolved.Count];
        var claimedEntries = new HashSet<AppEntry>(ReferenceEqualityComparer.Instance);

        // 第 1 趟：按(来源 + 相对路径)精确匹配。
        // 这是"磁盘上同一个文件"最精确的身份，必须最先用 ——
        // 否则多个来源里的同一个程序会抢同一个目标索引。
        for (var i = 0; i < resolved.Count; i++)
        {
            var location = LocationKey(resolved[i].Item.SourceId, resolved[i].Item.RelativePath);
            if (byLocation.TryGetValue(location, out var entry) && claimedEntries.Add(entry))
                assigned[i] = entry;
        }

        // 第 2 趟：按目标路径匹配还没被占用的条目（处理"快捷方式被改名"）
        for (var i = 0; i < resolved.Count; i++)
        {
            if (assigned[i] is not null)
                continue;

            var key = resolved[i].Seed.StableKey;
            if (string.IsNullOrEmpty(key) || !byStableKey.TryGetValue(key, out var candidates))
                continue;

            var candidate = candidates.FirstOrDefault(c => !claimedEntries.Contains(c));
            if (candidate is not null && claimedEntries.Add(candidate))
                assigned[i] = candidate;
        }

        // 第 3 趟：新建。
        // 已经被占用的目标路径直接丢弃 —— 那是跨来源的重复项。
        // 实测同一份开始菜单的两个来源（当前用户 / 所有用户）里，
        // 同一个程序平均出现 1.4 次，不去重界面会被重复卡片铺满。
        //
        // 注意 claimedKeys 只收"本轮匹配上了的"条目：一个已经失效的孤儿条目
        // 不该继续占着目标键，否则新出现的同名程序永远建不出来。
        var claimedKeys = new HashSet<string>(
            claimedEntries.Select(e => e.StableKey).Where(k => !string.IsNullOrEmpty(k)),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < resolved.Count; i++)
        {
            if (assigned[i] is not null)
                continue;

            var key = resolved[i].Seed.StableKey;
            if (!string.IsNullOrEmpty(key) && !claimedKeys.Add(key))
                continue;

            assigned[i] = CreateEntry(data, resolved[i].Seed, resolved[i].Item);
            isNewEntry[i] = true;
            data.Apps.Add(assigned[i]!);
        }

        // ---------- 3. 更新字段 ----------
        var matched = new HashSet<AppEntry>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < resolved.Count; i++)
        {
            var entry = assigned[i];
            if (entry is null || !matched.Add(entry))
                continue;   // 同一个条目被两个磁盘项匹配上了，别重复处理

            if (isNewEntry[i])
                added++;
            else
                updated++;

            var (item, seed) = resolved[i];

            entry.LaunchPath = seed.LaunchPath;
            entry.StableKey = seed.StableKey;
            entry.ScanSourceId = item.SourceId;
            entry.ScanRelativePath = item.RelativePath;
            entry.MissingSince = null;

            if (!entry.NameCustomized && !string.IsNullOrWhiteSpace(seed.DisplayName))
                entry.Name = seed.DisplayName;

            entry.CategoryIds = ResolveCategories(data, seed, item);
        }

        // ---------- 4. 磁盘上没有了 → ★ 只标记失效，不删除 ----------
        // 删掉的话，U 盘没插、网络盘断线都会导致简介/分类归属/自定义名永久丢失。
        foreach (var app in data.Apps)
        {
            if (app.Source != AppSource.Scan || matched.Contains(app))
                continue;

            // 保留最早的失效时刻，不要每次同步都刷新
            app.MissingSince ??= now;
            missing++;
        }

        // ---------- 5. 回收不再被任何条目引用的扫描分类 ----------
        PruneUnusedCategories(data);

        SortAll(data);

        return new ScanOutcome(added, updated, missing, matched.Count, filtered);
    }

    /// <summary>
    /// 决定一个应用属于哪些分类。
    /// 用户手动改过的（Knowledge 里有 CategoryOverride）一律以用户的为准 ——
    /// 否则你费劲拖进「开发工具」的东西，下次同步就被分类器拽回原位了。
    /// </summary>
    private static List<string> ResolveCategories(LauncherData data, EntrySeed seed, ScanItem item)
    {
        if (data.Knowledge.TryGetValue(seed.StableKey, out var knowledge)
            && knowledge.CategoryOverride is { Count: > 0 } overridden)
        {
            return [.. overridden];
        }

        return CategoryClassifier.Classify(seed.DisplayName, item.FolderName, seed.StableKey);
    }

    private static AppEntry CreateEntry(LauncherData data, EntrySeed seed, ScanItem item)
    {
        var entry = new AppEntry
        {
            Source = AppSource.Scan,
            LaunchPath = seed.LaunchPath,
            StableKey = seed.StableKey,
            ScanSourceId = item.SourceId,
            ScanRelativePath = item.RelativePath,
            Name = seed.DisplayName,
        };

        // ★ 知识库回填：这个程序以前出现过、用户改过名字或写过简介，重新出现时自动带回来。
        //   这是"删掉快捷方式再加回来，简介还在"的实现点。
        if (data.Knowledge.TryGetValue(seed.StableKey, out var knowledge)
            && !string.IsNullOrWhiteSpace(knowledge.Alias))
        {
            entry.Name = knowledge.Alias;
            entry.NameCustomized = true;
        }

        entry.Description = EntryFactory.SeedDescription(data, seed.StableKey);

        return entry;
    }

    /// <summary>
    /// 这个名字/目标看起来是不是"卸载/帮助/文档"这类不该进启动器的快捷方式。
    /// </summary>
    public static bool IsNoise(string baseName, string? targetPath = null)
    {
        // 目标是个文档文件 —— 这条最准，一个不漏
        if (!string.IsNullOrEmpty(targetPath))
        {
            var extension = Path.GetExtension(targetPath);
            if (!string.IsNullOrEmpty(extension) && DocumentExtensions.Contains(extension))
                return true;
        }

        if (string.IsNullOrWhiteSpace(baseName))
            return false;

        // allowPlural：让 "release note" 能匹配上 "Release Notes"、"manual" 匹配上 "Manuals"
        return KeywordMatcher.MatchesAny(baseName.ToLowerInvariant(), NoiseMarkers, allowPlural: true);
    }

    /// <summary>把不再被任何条目引用的扫描分类收掉。手建分类和内置分类永不动。</summary>
    private static void PruneUnusedCategories(LauncherData data)
    {
        var used = new HashSet<string>(
            data.Apps.SelectMany(a => a.CategoryIds),
            StringComparer.OrdinalIgnoreCase);

        data.Categories.RemoveAll(c =>
            c.Source == AppSource.Scan
            && !c.IsBuiltIn
            && !c.SharedWithScan
            && !used.Contains(c.Id));
    }

    /// <summary>
    /// 分类按 Order 再按名称；应用按名称（中文用当前区域性排序，符合直觉）。
    /// 内置分类的 Order 是固定序号，所以顺序永远是「开发工具 → 游戏 → … → 其他」，
    /// 不会随名字的字母序乱跳。
    /// </summary>
    public static void SortAll(LauncherData data)
    {
        data.Categories.Sort((a, b) =>
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0
                ? byOrder
                : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
        });

        data.Apps.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
    }

    private static string LocationKey(string? sourceId, string? relativePath)
        => string.IsNullOrEmpty(relativePath)
            ? ""
            : (sourceId ?? "") + "|" + relativePath;

    private static string MakeRelative(string root, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(root, fullPath).ToLowerInvariant();
        }
        catch
        {
            return fullPath.ToLowerInvariant();
        }
    }
}
