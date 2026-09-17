using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinLauncher.Models;

namespace WinLauncher.Services;

/// <summary>
/// launcher.json 的读写。
/// 写入走"临时文件 + 原子替换"，避免写到一半崩溃导致整个配置损坏。
/// </summary>
public sealed class DataStore
{
    public LauncherData Data { get; private set; } = new();

    /// <summary>上次加载时主文件损坏、从备份恢复；界面可据此提示用户。</summary>
    public bool RecoveredFromBackup { get; private set; }

    /// <summary>这次加载做过结构升级；界面可据此提示"已按新规则重新归类"。</summary>
    public bool Migrated { get; private set; }

    public void Load()
    {
        AppPaths.EnsureCreated();

        Data = LoadCore();

        // ⚠️ 配置补齐必须放在所有分支外面。
        // 之前它写在 TryRead 里，导致"首次运行没有配置文件"这条路径上
        // 扫描来源是空的，自动扫描静默跳过。
        MigrateScanConfig(Data);
        CategoryClassifier.EnsureBuiltInCategories(Data);
    }

    private LauncherData LoadCore()
    {
        var primary = TryRead(AppPaths.DataFile);
        if (primary is not null)
            return primary;

        // 主文件不存在或已损坏
        if (File.Exists(AppPaths.DataFile))
        {
            // 留证据再挪走，不要静默覆盖用户数据
            try
            {
                var quarantine = AppPaths.DataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(AppPaths.DataFile, quarantine, overwrite: true);
            }
            catch
            {
                // 挪不动就算了，后面会被覆盖
            }

            var backup = TryRead(AppPaths.BackupFile);
            if (backup is not null)
            {
                RecoveredFromBackup = true;
                return backup;
            }
        }

        return new LauncherData();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();

        Data.Version = LauncherData.CurrentVersion;

        var json = JsonSerializer.Serialize(Data, AppPaths.JsonOptions);
        // UTF-8 无 BOM：BOM 会让手工编辑配置的编辑器显示乱码
        File.WriteAllText(AppPaths.TempFile, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            if (File.Exists(AppPaths.DataFile))
            {
                // 原子替换，并把旧版本自动留成 .bak
                File.Replace(AppPaths.TempFile, AppPaths.DataFile, AppPaths.BackupFile,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(AppPaths.TempFile, AppPaths.DataFile);
            }
        }
        catch (IOException)
        {
            // 少数文件系统（网络盘、部分虚拟盘）不支持 File.Replace，退化为普通覆盖
            File.Copy(AppPaths.TempFile, AppPaths.DataFile, overwrite: true);
            try { File.Delete(AppPaths.TempFile); } catch { }
        }
    }

    // ==================== 读取与结构迁移 ====================

    /// <summary>
    /// 磁盘上的原始结构。刻意保留 v1/v2 才有的字段（<see cref="MigrationCategory.Apps"/>），
    /// 否则旧配置读进来时那些字段会被静默丢掉 —— 用户的应用列表直接消失。
    /// </summary>
    private sealed class MigrationDto
    {
        public int Version { get; set; }
        public List<MigrationCategory>? Categories { get; set; }
        public List<AppEntry>? Apps { get; set; }
        public ScanConfig? Scan { get; set; }
        public AppSettings? Settings { get; set; }
        public Dictionary<string, AppKnowledge>? Knowledge { get; set; }
    }

    private sealed class MigrationCategory
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Order { get; set; }
        public AppSource Source { get; set; }
        public bool SharedWithScan { get; set; }
        public bool IsBuiltIn { get; set; }

        /// <summary>v2 及以前：应用是装在分类里的。v3 之后不再有。</summary>
        public List<AppEntry>? Apps { get; set; }
    }

    private LauncherData? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var dto = JsonSerializer.Deserialize<MigrationDto>(json, AppPaths.JsonOptions);
            if (dto is null)
                return null;

            return Convert(dto);
        }
        catch
        {
            return null;
        }
    }

    private LauncherData Convert(MigrationDto dto)
    {
        var data = new LauncherData
        {
            Version = dto.Version,
            Scan = dto.Scan ?? new ScanConfig(),
            Settings = dto.Settings ?? new AppSettings(),
            Knowledge = new Dictionary<string, AppKnowledge>(
                dto.Knowledge ?? [], StringComparer.OrdinalIgnoreCase),
        };

        data.Scan.Sources ??= [];

        var categories = dto.Categories ?? [];

        // ---- 摊平应用 ----
        // v3：应用在顶层，归属由 CategoryIds 表达
        // v2 及以前：应用装在各个分类里，需要摊平并补上归属
        if (dto.Apps is { Count: > 0 })
        {
            data.Apps = dto.Apps;
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var category in categories)
            {
                if (category.Apps is not { Count: > 0 })
                    continue;

                foreach (var app in category.Apps)
                {
                    // 同一个应用可能出现在多个分类里（旧结构下这是复制出来的独立对象），
                    // 按 Id 去重，只保留第一份
                    if (!string.IsNullOrEmpty(app.Id) && !seen.Add(app.Id))
                        continue;

                    if (app.CategoryIds.Count == 0)
                        app.CategoryIds = [category.Id];

                    data.Apps.Add(app);
                }
            }

            if (data.Apps.Count > 0)
                Migrated = true;
        }

        foreach (var category in categories)
        {
            data.Categories.Add(new Category
            {
                Id = string.IsNullOrEmpty(category.Id) ? Guid.NewGuid().ToString("N") : category.Id,
                Name = category.Name,
                Order = category.Order,
                Source = category.Source,
                SharedWithScan = category.SharedWithScan,
                IsBuiltIn = category.IsBuiltIn,
            });
        }

        foreach (var app in data.Apps)
            app.CategoryIds ??= [];

        UpgradeToV3(data);

        return data;
    }

    /// <summary>
    /// v2 → v3：分类从"照搬开始菜单文件夹名"改成"语义分类"。
    ///
    /// 旧分类全部丢掉由分类器重建（它们本来就不是分类，是厂商目录名）；
    /// 扫描来源的条目清空归属，下次同步时重新归类。
    /// 手建分类、简介、别名一律保留。
    /// </summary>
    private static void UpgradeToV3(LauncherData data)
    {
        if (data.Version >= LauncherData.CurrentVersion)
        {
            EnsureEveryAppHasCategory(data);
            return;
        }

        // 旧分类是厂商目录名，留着只会污染新分类集。手建的留下。
        data.Categories.RemoveAll(c => c.Source == AppSource.Scan && !c.SharedWithScan);

        foreach (var app in data.Apps)
        {
            if (app.Source != AppSource.Scan)
                continue;

            // 用户手动改过归属的不动
            if (data.Knowledge.TryGetValue(app.StableKey, out var knowledge)
                && knowledge.CategoryOverride is { Count: > 0 })
            {
                continue;
            }

            app.CategoryIds.Clear();
        }

        data.Version = LauncherData.CurrentVersion;
        EnsureEveryAppHasCategory(data);
    }

    /// <summary>兜底：任何应用都至少要有一个分类，否则它在界面上就彻底看不见了。</summary>
    private static void EnsureEveryAppHasCategory(LauncherData data)
    {
        foreach (var app in data.Apps)
        {
            if (app.CategoryIds.Count == 0)
                app.CategoryIds.Add(CategoryClassifier.Other);
        }
    }

    /// <summary>
    /// 补齐扫描来源配置。
    ///
    /// 两件事：
    ///   1. 从没配过来源 → 建成默认的自动发现来源（开始菜单 + 桌面）。
    ///   2. 老版本存过单一扫描根 → 转成自定义来源保留下来，别静默丢掉。
    /// </summary>
    private static void MigrateScanConfig(LauncherData data)
    {
        data.Scan.Sources ??= [];

        // v1 → v2：单一扫描根转成自定义来源
        if (!string.IsNullOrWhiteSpace(data.Scan.RootPath))
        {
            var legacyPath = data.Scan.RootPath.Trim();
            var id = ScanSourceResolver.SourceIdFor(ScanSourceKind.CustomFolder, legacyPath);

            if (data.Scan.Sources.All(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                data.Scan.Sources.Add(new ScanSource
                {
                    Id = id,
                    Kind = ScanSourceKind.CustomFolder,
                    CustomPath = legacyPath,
                    DisplayName = $"自定义目录（{Path.GetFileName(legacyPath.TrimEnd(Path.DirectorySeparatorChar))}）",
                });
            }

            data.Scan.RootPath = null;
        }

        if (data.Scan.Sources.Count == 0)
            data.Scan.Sources = ScanSourceResolver.CreateDefaults();
    }
}
