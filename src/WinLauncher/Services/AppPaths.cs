using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinLauncher.Services;

/// <summary>
/// 所有磁盘路径的唯一来源。
/// 数据放 %APPDATA%\AppLauncher\：开发调试时 exe 在 bin\Debug 下、发布后在别处，
/// 但配置始终是同一份，不会出现"调试时配好了、发布后全没了"。
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppLauncher");

    public static string DataFile => Path.Combine(DataRoot, "launcher.json");

    public static string BackupFile => Path.Combine(DataRoot, "launcher.json.bak");

    public static string TempFile => Path.Combine(DataRoot, "launcher.json.tmp");

    /// <summary>提取出来的 PNG 图标缓存。</summary>
    public static string IconCacheDir => Path.Combine(DataRoot, "IconCache");

    /// <summary>从桌面拖进来时复制过来的 .lnk 副本。</summary>
    public static string ShortcutsDir => Path.Combine(DataRoot, "shortcuts");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(IconCacheDir);
        Directory.CreateDirectory(ShortcutsDir);
    }

    /// <summary>
    /// 全局 JSON 配置。UnsafeRelaxedJsonEscaping 是必须的 ——
    /// 默认编码器会把中文简介写成 \uXXXX，配置文件将无法阅读和手工编辑。
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
