using WinLauncher.Models;

namespace WinLauncher.Services;

/// <summary>把 <see cref="ScanSource"/> 解析成实际路径，并提供默认来源集合。</summary>
public static class ScanSourceResolver
{
    /// <summary>
    /// 解析来源的根目录。用 SpecialFolder 而不是硬编码路径 ——
    /// OneDrive 会重定向桌面和文档，硬编码会找不到。
    /// </summary>
    public static string? ResolvePath(ScanSource source)
    {
        try
        {
            var path = source.Kind switch
            {
                ScanSourceKind.StartMenuCommon => ProgramsFolder(Environment.SpecialFolder.CommonStartMenu),
                ScanSourceKind.StartMenuUser => ProgramsFolder(Environment.SpecialFolder.StartMenu),
                ScanSourceKind.DesktopCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                ScanSourceKind.DesktopUser => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                ScanSourceKind.CustomFolder => source.CustomPath,
                _ => null,
            };

            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>开始菜单的路径指向 "Start Menu" 本身，程序都在下面的 "Programs" 里。</summary>
    private static string ProgramsFolder(Environment.SpecialFolder folder)
    {
        var startMenu = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(startMenu))
            return "";

        var programs = Path.Combine(startMenu, "Programs");
        // 少数精简系统没有 Programs 这一层
        return Directory.Exists(programs) ? programs : startMenu;
    }

    /// <summary>
    /// 默认来源。自动发现的来源默认全开 —— 用户要的就是"装上就能用，不用先配置"。
    ///
    /// 顺序即优先级：同一个程序在多处出现时，靠前的来源胜出（先处理先占位）。
    /// 当前用户的开始菜单排在所有用户之前，因为那更能反映"我自己装的"。
    /// </summary>
    public static List<ScanSource> CreateDefaults()
    {
        var sources = new List<ScanSource>
        {
            new() { Kind = ScanSourceKind.StartMenuUser, DisplayName = "我的开始菜单" },
            new() { Kind = ScanSourceKind.StartMenuCommon, DisplayName = "所有用户的开始菜单" },
            new() { Kind = ScanSourceKind.DesktopUser, DisplayName = "我的桌面" },
            new() { Kind = ScanSourceKind.DesktopCommon, DisplayName = "公共桌面" },
        };

        foreach (var source in sources)
            source.Id = SourceIdFor(source.Kind, null);

        return sources;
    }

    public static string SourceIdFor(ScanSourceKind kind, string? customPath)
        => kind == ScanSourceKind.CustomFolder
            ? $"CustomFolder:{ShortHash(customPath ?? "")}"
            : kind.ToString();

    /// <summary>来源的友好名字，界面和日志都用它。</summary>
    public static string DescribeKind(ScanSourceKind kind) => kind switch
    {
        ScanSourceKind.StartMenuUser => "我的开始菜单",
        ScanSourceKind.StartMenuCommon => "所有用户的开始菜单",
        ScanSourceKind.DesktopUser => "我的桌面",
        ScanSourceKind.DesktopCommon => "公共桌面",
        ScanSourceKind.CustomFolder => "自定义目录",
        _ => kind.ToString(),
    };

    /// <summary>这个来源在当前机器上是否真的存在（路径不存在就别显示在设置里）。</summary>
    public static bool Exists(ScanSource source)
    {
        var path = ResolvePath(source);
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    }

    private static string ShortHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }
}
