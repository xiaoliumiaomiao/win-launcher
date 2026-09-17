using WinLauncher.Models;

namespace WinLauncher.Services;

public sealed record AddResult(int Added, int Duplicate, int Unsupported, int Failed)
{
    public bool AnyFailed => Failed > 0 || Unsupported > 0;
}

/// <summary>
/// 用户直接操作的入口：加应用、删应用、改简介、改分类归属、建分类。
///
/// 所有改动都同时写进 <see cref="LauncherData.Knowledge"/>，
/// 这样条目被删掉再加回来时，简介、自定义名和分类归属都能自动恢复。
/// </summary>
public static class LibraryService
{
    public static AddResult AddFiles(LauncherData data, Category category, IEnumerable<string> paths)
    {
        var added = 0;
        var duplicate = 0;
        var unsupported = 0;
        var failed = 0;

        var knownKeys = new HashSet<string>(
            data.Apps.Select(a => a.StableKey).Where(k => !string.IsNullOrEmpty(k)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path) || !EntryFactory.IsSupported(path))
                {
                    unsupported++;
                    continue;
                }

                var seed = EntryFactory.Resolve(path, copyShortcutToManagedDir: true);
                if (seed is null)
                {
                    failed++;
                    continue;
                }

                if (!string.IsNullOrEmpty(seed.StableKey) && !knownKeys.Add(seed.StableKey))
                {
                    // 已经在列表里了 —— 把刚复制出来的副本清掉，别留垃圾文件
                    DeleteManagedCopy(seed.LaunchPath);
                    duplicate++;
                    continue;
                }

                var entry = new AppEntry
                {
                    Source = AppSource.Manual,
                    LaunchPath = seed.LaunchPath,
                    StableKey = seed.StableKey,
                    Name = seed.DisplayName,
                    Description = EntryFactory.SeedDescription(data, seed.StableKey),
                    CategoryIds = [category.Id],
                };

                // 以前归类过同一个程序，就沿用当时的归属
                if (data.Knowledge.TryGetValue(seed.StableKey, out var knowledge)
                    && knowledge.CategoryOverride is { Count: > 0 } overridden)
                {
                    entry.CategoryIds = [.. overridden];
                }

                data.Apps.Add(entry);
                added++;
            }
            catch
            {
                failed++;
            }
        }

        ScanService.SortAll(data);
        return new AddResult(added, duplicate, unsupported, failed);
    }

    public static void SetDescription(LauncherData data, AppEntry entry, string description)
    {
        entry.Description = description ?? "";
        Remember(data, entry);
    }

    public static void SetName(LauncherData data, AppEntry entry, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        entry.Name = name.Trim();
        entry.NameCustomized = true;
        Remember(data, entry);
        ScanService.SortAll(data);
    }

    /// <summary>
    /// 改一个应用属于哪些分类。传进来的就是完整的新归属集合（不是增量）。
    ///
    /// 会写进 Knowledge.CategoryOverride，所以以后重新扫描不会再把它自动归类回去 ——
    /// 否则你手动移动过的应用每次同步都会被分类器拽走。
    /// </summary>
    public static void SetCategories(LauncherData data, AppEntry entry, IEnumerable<string> categoryIds)
    {
        var ids = categoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 一个都不留的话应用就从所有分类里消失了，界面上再也看不到它
        if (ids.Count == 0)
            return;

        entry.CategoryIds = ids;
        Remember(data, entry);
    }

    /// <summary>
    /// 从列表移除。会删掉托管目录里的 .lnk 副本，但**绝不碰源文件**。
    /// Knowledge 刻意保留：以后再加回来时简介和分类归属还在。
    /// </summary>
    public static void RemoveEntry(LauncherData data, AppEntry entry)
    {
        data.Apps.Remove(entry);
        DeleteManagedCopy(entry.LaunchPath);

        // 扫描来源的条目如果只是"这次没扫到"，删掉后下次同步会重新出现；
        // 这里同时把它的失效标记清掉，避免重新出现时仍显示灰色
        entry.MissingSince = null;
    }

    public static Category AddCategory(LauncherData data, string name)
    {
        var category = new Category
        {
            Name = name.Trim(),
            Source = AppSource.Manual,
            // 内置分类占 10~1000，手建的排在它们后面
            Order = NextCategoryOrder(data),
        };

        data.Categories.Add(category);
        ScanService.SortAll(data);
        return category;
    }

    public static void RenameCategory(Category category, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        category.Name = name.Trim();
    }

    /// <summary>
    /// 删除分类。**只摘标签，不删应用** —— 应用本身还在，只是换到「其他」去。
    /// </summary>
    public static void RemoveCategory(LauncherData data, Category category)
    {
        var removalIds = new HashSet<string>([category.Id], StringComparer.OrdinalIgnoreCase);

        foreach (var app in data.Apps)
        {
            var before = app.CategoryIds.Count;
            app.CategoryIds.RemoveAll(id => removalIds.Contains(id));

            // 摘完没地方去了，扔进「其他」，否则这个应用在界面上就消失了
            if (app.CategoryIds.Count == 0)
                app.CategoryIds.Add(CategoryClassifier.Other);
            else if (app.CategoryIds.Count != before)
                Remember(data, app);   // 归属变了，把 override 同步一下
        }

        data.Categories.Remove(category);

        // 手动覆盖里可能还残留着这个已删分类的 id，清掉
        foreach (var knowledge in data.Knowledge.Values)
        {
            if (knowledge.CategoryOverride is not { Count: > 0 } overridden)
                continue;

            var cleaned = overridden.Where(id => !removalIds.Contains(id)).ToList();
            if (cleaned.Count == 0)
                cleaned.Add(CategoryClassifier.Other);

            knowledge.CategoryOverride = cleaned;
        }

        ScanService.SortAll(data);
    }

    /// <summary>把当前简介/别名写回知识库，供条目日后重新出现时回填。</summary>
    private static void Remember(LauncherData data, AppEntry entry)
    {
        if (string.IsNullOrEmpty(entry.StableKey))
            return;

        if (!data.Knowledge.TryGetValue(entry.StableKey, out var knowledge))
            data.Knowledge[entry.StableKey] = knowledge = new AppKnowledge();

        knowledge.Description = string.IsNullOrWhiteSpace(entry.Description)
            ? null
            : entry.Description;

        knowledge.Alias = entry.NameCustomized && !string.IsNullOrWhiteSpace(entry.Name)
            ? entry.Name
            : null;

        knowledge.CategoryOverride = entry.CategoryIds.Count > 0
            ? [.. entry.CategoryIds]
            : null;
    }

    /// <summary>只删托管目录里的副本。源文件、扫描目录里的文件一律不动。</summary>
    private static void DeleteManagedCopy(string launchPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(launchPath);
            if (directory is null)
                return;

            if (!directory.Equals(AppPaths.ShortcutsDir, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(launchPath))
                File.Delete(launchPath);
        }
        catch
        {
            // 删不掉就留着，不影响功能
        }
    }

    private static int NextCategoryOrder(LauncherData data)
        => data.Categories.Count == 0 ? 2000 : data.Categories.Max(c => c.Order) + 1;
}
