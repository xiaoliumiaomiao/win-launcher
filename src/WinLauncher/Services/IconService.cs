using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinLauncher.Interop;

namespace WinLauncher.Services;

/// <summary>
/// 图标提取 + 两级缓存（内存 + 磁盘 PNG）。
///
/// 提取跑在专用 STA 线程上而不是线程池：IShellItemImageFactory 背后是 shell COM 组件，
/// 部分 shell 扩展要求 STA 单元，在线程池的 MTA 线程上调用会偶发失败或死锁。
/// 为几十个图标各建一次线程不划算，所以用一个常驻工作线程 + 队列。
/// </summary>
public sealed class IconService : IDisposable
{
    /// <summary>
    /// 统一按 256px 提取。这是 shell 的 jumbo 上限，一次提取就能覆盖 400% 缩放以内
    /// 的所有显示需求，不用按 DPI 分别提取多份。
    /// </summary>
    private const int IconSize = 256;

    private readonly ConcurrentDictionary<string, BitmapSource> _memory =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly BlockingCollection<IconRequest> _queue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _worker;

    private sealed record IconRequest(string Path, TaskCompletionSource<BitmapSource?> Completion);

    public IconService()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "IconExtractor",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>取图标。命中内存缓存时同步返回，否则排队异步提取。</summary>
    public Task<BitmapSource?> GetAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<BitmapSource?>(null);

        if (_memory.TryGetValue(path, out var cached))
            return Task.FromResult<BitmapSource?>(cached);

        var tcs = new TaskCompletionSource<BitmapSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(new IconRequest(path, tcs));
        return tcs.Task;
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    request.Completion.TrySetResult(LoadOrExtract(request.Path));
                }
                catch
                {
                    // 单个图标失败不该拖垮整条队列
                    request.Completion.TrySetResult(null);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }

    private BitmapSource? LoadOrExtract(string path)
    {
        var pngPath = CacheFilePath(path);

        var fromDisk = TryReadPng(pngPath);
        if (fromDisk is not null)
        {
            _memory[path] = fromDisk;
            return fromDisk;
        }

        var extracted = Extract(path);
        if (extracted is null)
            return null;

        _memory[path] = extracted;
        TryWritePng(extracted, pngPath);
        return extracted;
    }

    /// <summary>
    /// 缓存文件名 = SHA256(路径 + 文件大小 + 修改时间)。
    /// 目标程序升级后修改时间变、哈希就变，缓存自动失效，不需要额外的失效判断逻辑。
    /// </summary>
    private static string CacheFilePath(string path)
    {
        var stamp = "missing";
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                stamp = $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            else if (Directory.Exists(path))
            {
                stamp = "dir";
            }
        }
        catch
        {
            // 拿不到文件信息就用 missing 占位
        }

        var raw = $"{path.ToLowerInvariant()}|{stamp}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return Path.Combine(AppPaths.IconCacheDir, hash + ".png");
    }

    private static BitmapSource? Extract(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return null;

            var iid = NativeMethods.IID_IShellItemImageFactory;
            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);

            try
            {
                // ICONONLY 阻止 shell 去生成文档缩略图；BIGGERSIZEOK 让它返回可用的最大图标。
                // 不加 SCALEUP —— 让 WPF 用 HighQuality 模式缩放，质量比 shell 的放大好。
                var hr = factory.GetImage(
                    new NativeMethods.SIZE(IconSize, IconSize),
                    NativeMethods.SIIGBF.ICONONLY | NativeMethods.SIIGBF.BIGGERSIZEOK,
                    out var hbm);

                if (hr != 0 || hbm == IntPtr.Zero)
                    return null;

                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hbm,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // ⚠️ 必须先 Freeze 再让下面的 finally 释放 HBITMAP。
                    //    CreateBitmapSourceFromHBitmap 创建的是"包着"HBITMAP 的位图，
                    //    提前 DeleteObject 会得到空白图甚至访问违例；Freeze 之后数据才归 WPF 所有。
                    source.Freeze();
                    return source;
                }
                finally
                {
                    NativeMethods.DeleteObject(hbm);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryReadPng(string pngPath)
    {
        try
        {
            if (!File.Exists(pngPath))
                return null;

            using var stream = File.OpenRead(pngPath);
            var image = new BitmapImage();
            image.BeginInit();
            // ⚠️ 必须 OnLoad：否则 BitmapImage 会一直占着文件句柄，
            //    后续重建缓存时删不掉旧 PNG。
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            // 缓存文件损坏，删掉重来
            try { File.Delete(pngPath); } catch { }
            return null;
        }
    }

    private static void TryWritePng(BitmapSource bitmap, string pngPath)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(pngPath);
            encoder.Save(stream);
        }
        catch
        {
            // 缓存写失败不影响使用，下次重新提取即可
        }
    }

    /// <summary>清空磁盘图标缓存（设置面板用）。内存缓存一并清掉。</summary>
    public void ClearDiskCache()
    {
        _memory.Clear();
        try
        {
            if (!Directory.Exists(AppPaths.IconCacheDir))
                return;

            foreach (var file in Directory.EnumerateFiles(AppPaths.IconCacheDir, "*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // 目录被占用等情况忽略
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        try { _cts.Cancel(); } catch { }
        // 不 Join：后台线程是 IsBackground，随进程退出即可，
        // Join 反而可能在某个 shell 调用卡住时挂死关闭流程。
        _cts.Dispose();
        _queue.Dispose();
    }
}
