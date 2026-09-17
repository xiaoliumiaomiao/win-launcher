using System.Windows.Interop;
using WinLauncher.Interop;

namespace WinLauncher.Services;

/// <summary>
/// 全局热键。
///
/// 挂在一个独立的不可见消息窗口上，而不是主窗口 ——
/// 这样主窗口 Hide 到托盘、甚至被重建，热键都不受影响。
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0x4C41;   // 任意值，单实例下不会冲突

    private readonly HwndSource _messageWindow;
    private bool _registered;
    private bool _disposed;

    /// <summary>热键被按下。在 UI 线程上触发。</summary>
    public event Action? Pressed;

    public HotKeyService()
    {
        _messageWindow = new HwndSource(new HwndSourceParameters("WinLauncherHotKeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000),   // WS_POPUP，不可见
        });
        _messageWindow.AddHook(WndProc);
    }

    /// <summary>注册热键。返回 false 表示被别的软件占用了，调用方应提示用户换一个。</summary>
    public bool TryRegister(string hotKeyText)
    {
        if (!HotKeyParser.TryParse(hotKeyText, out var modifiers, out var virtualKey))
            return false;

        Unregister();

        _registered = NativeMethods.RegisterHotKey(
            _messageWindow.Handle,
            HotKeyId,
            // MOD_NOREPEAT：按住不放时不重复触发，否则会疯狂开合窗口
            modifiers | NativeMethods.MOD_NOREPEAT,
            virtualKey);

        return _registered;
    }

    private void Unregister()
    {
        if (!_registered)
            return;

        NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotKeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}

/// <summary>把 "Alt+." 这样的文本解析成 RegisterHotKey 需要的修饰符和虚拟键码。</summary>
public static class HotKeyParser
{
    /// <summary>
    /// 符号键到虚拟键码的映射。
    /// 不能用字符本身的 ASCII —— 这些键在键盘上是"OEM 键"，
    /// 位置随键盘布局变化，必须用 VK_OEM_* 系列常量。
    /// </summary>
    private static readonly Dictionary<char, uint> SymbolKeys = new()
    {
        ['.'] = 0xBE,   // VK_OEM_PERIOD
        [','] = 0xBC,   // VK_OEM_COMMA
        [';'] = 0xBA,   // VK_OEM_1
        ['/'] = 0xBF,   // VK_OEM_2
        ['`'] = 0xC0,   // VK_OEM_3
        ['['] = 0xDB,   // VK_OEM_4
        ['\\'] = 0xDC,  // VK_OEM_5
        [']'] = 0xDD,   // VK_OEM_6
        ['\''] = 0xDE,  // VK_OEM_7
        ['-'] = 0xBD,   // VK_OEM_MINUS
        ['='] = 0xBB,   // VK_OEM_PLUS
        [' '] = 0x20,   // VK_SPACE
    };

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;

            if (!isLast)
            {
                switch (part.ToLowerInvariant())
                {
                    case "alt": modifiers |= NativeMethods.MOD_ALT; break;
                    case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; break;
                    case "shift": modifiers |= NativeMethods.MOD_SHIFT; break;
                    case "win": modifiers |= NativeMethods.MOD_WIN; break;
                    default: return false;
                }
                continue;
            }

            virtualKey = ParseKey(part);
            if (virtualKey == 0)
                return false;
        }

        // 必须至少有一个修饰键，否则会抢掉普通打字
        return modifiers != 0 && virtualKey != 0;
    }

    private static uint ParseKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);

            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;

            if (SymbolKeys.TryGetValue(key[0], out var symbolKey))
                return symbolKey;
        }

        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var fn)
            && fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);   // VK_F1 = 0x70
        }

        return 0;
    }
}
