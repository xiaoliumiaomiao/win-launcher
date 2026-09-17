using System.Runtime.InteropServices;
using System.Text;
using WinLauncher.Interop;

namespace WinLauncher.Services;

/// <summary>快捷方式解析结果。</summary>
/// <param name="Target">真实目标路径。UWP/特殊快捷方式下可能为空字符串。</param>
/// <param name="Arguments">启动参数。</param>
/// <param name="WorkingDirectory">工作目录。</param>
/// <param name="Resolved">是否成功解析出非空 Target。</param>
public sealed record ResolvedShortcut(
    string Target,
    string Arguments,
    string WorkingDirectory,
    bool Resolved);

/// <summary>把 .lnk / .url 解析成可启动的目标。</summary>
public static class ShortcutResolver
{
    /// <summary>
    /// 解析 .lnk。
    ///
    /// 全程带 SLR_NO_UI | SLR_NOSEARCH | SLR_NOTRACK：目标程序已被卸载时，
    /// 不带这些标志 shell 会弹"找不到目标"的模态框并挂住调用线程，
    /// 而这个方法是在后台线程上跑的，挂住就再也回不来了。
    /// </summary>
    public static ResolvedShortcut Resolve(string lnkPath)
    {
        object? shellLinkObj = null;
        object? persistFileObj = null;
        try
        {
            shellLinkObj = ShellLinkInterop.CreateShellLinkInstance();
            var link = (IShellLinkW)shellLinkObj;
            persistFileObj = shellLinkObj;
            var persist = (IPersistFile)persistFileObj;

            persist.Load(lnkPath, ShellLinkInterop.STGM_READ);

            // 死链时 Resolve 会返回非 0，忽略即可 —— GetPath 依然能给出链接里存的路径
            _ = link.Resolve(IntPtr.Zero,
                ShellLinkInterop.SLR_NO_UI |
                ShellLinkInterop.SLR_NOSEARCH |
                ShellLinkInterop.SLR_NOTRACK);

            // GetPath 多一个 fFlags 参数，和其他 getter 签名不同，单独处理
            var pathBuf = new StringBuilder(1024);
            var target = link.GetPath(pathBuf, pathBuf.Capacity, IntPtr.Zero, ShellLinkInterop.SLR_NO_UI) == 0
                ? pathBuf.ToString()
                : "";

            var argsBuf = new StringBuilder(1024);
            link.GetArguments(argsBuf, argsBuf.Capacity);

            var wdBuf = new StringBuilder(1024);
            link.GetWorkingDirectory(wdBuf, wdBuf.Capacity);

            var args = argsBuf.ToString();
            var workDir = wdBuf.ToString();

            return new ResolvedShortcut(
                target,
                args,
                workDir,
                Resolved: !string.IsNullOrWhiteSpace(target));
        }
        catch (Exception)
        {
            // 快捷方式损坏、CLSID 注册缺失等，一律当作"解析不出目标"，由调用方回退
            return new ResolvedShortcut("", "", "", Resolved: false);
        }
        finally
        {
            if (persistFileObj is not null && Marshal.IsComObject(persistFileObj))
                Marshal.FinalReleaseComObject(persistFileObj);
            if (shellLinkObj is not null && !ReferenceEquals(shellLinkObj, persistFileObj)
                && Marshal.IsComObject(shellLinkObj))
                Marshal.FinalReleaseComObject(shellLinkObj);
        }
    }

    /// <summary>
    /// 读取 .url（Internet 快捷方式）。它就是个 INI 文本，不需要 COM。
    /// </summary>
    public static string? ReadUrlFile(string urlPath)
    {
        try
        {
            foreach (var raw in File.ReadLines(urlPath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..].Trim();
            }
        }
        catch
        {
            // 文件读不了就当解析失败
        }

        return null;
    }

    /// <summary>把解析结果规范化成去重用的稳定键。</summary>
    public static string NormalizeStableKey(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "";

        // URL 不做路径规范化（Uri 规范化会改写末尾斜杠等，反而不稳定）
        if (target.Contains("://", StringComparison.Ordinal))
            return target.Trim().ToLowerInvariant();

        var expanded = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(expanded).ToLowerInvariant();
        }
        catch
        {
            return expanded.ToLowerInvariant();
        }
    }
}
