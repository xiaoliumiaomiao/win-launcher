using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinLauncher.Models;
using WinLauncher.Services;

namespace WinLauncher.Views;

public partial class SettingsDialog : Window
{
    private readonly DataStore _store;
    private readonly IconService _icons;
    private readonly string _originalHotKey;

    /// <summary>只有本机真实存在的来源才显示出来，免得列一堆用不上的选项。</summary>
    private readonly List<ScanSource> _sources;

    public SettingsDialog(DataStore store, IconService icons)
    {
        _store = store;
        _icons = icons;

        InitializeComponent();

        _originalHotKey = store.Data.Settings.HotKey;

        _sources = store.Data.Scan.Sources
            .Where(s => s.Kind == ScanSourceKind.CustomFolder || ScanSourceResolver.Exists(s))
            .ToList();

        SourceList.ItemsSource = _sources;

        FilterNoiseBox.IsChecked = store.Data.Scan.FilterNoise;
        UpdateCustomRootBox();
        HotKeyBox.Text = _originalHotKey;
        HideAfterLaunchBox.IsChecked = store.Data.Settings.HideAfterLaunch;
        DataPathText.Text = AppPaths.DataRoot;
    }

    /// <summary>热键被改过，调用方需要重新注册。</summary>
    public bool HotKeyChanged { get; private set; }

    /// <summary>用户清空了图标缓存，调用方需要让卡片重新提取图标。</summary>
    public bool IconsCleared { get; private set; }

    private ScanSource? CustomSource
        => _sources.FirstOrDefault(s => s.Kind == ScanSourceKind.CustomFolder);

    private void UpdateCustomRootBox()
        => CustomRootBox.Text = CustomSource?.CustomPath ?? "";

    // ==================== 自定义目录 ====================

    private void OnBrowseCustomRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要自动收录的目录",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var picked = dialog.FolderName;

        // 防手滑：误选 C:\ 会扫出上百个分类，而且每次同步都很慢
        var subfolderCount = CountSubfolders(picked);
        if (subfolderCount > 200)
        {
            var answer = MessageBox.Show(this,
                $"这个目录下有 {subfolderCount} 个子文件夹，会生成同样多的分类。\n\n"
                + "确定要用它吗？（通常应该选一个专门放快捷方式的目录）",
                "应用启动器", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        var existing = CustomSource;
        if (existing is not null)
            _sources.Remove(existing);

        _sources.Add(new ScanSource
        {
            Id = ScanSourceResolver.SourceIdFor(ScanSourceKind.CustomFolder, picked),
            Kind = ScanSourceKind.CustomFolder,
            CustomPath = picked,
            Enabled = true,
            DisplayName = $"自定义目录（{Path.GetFileName(picked.TrimEnd(Path.DirectorySeparatorChar))}）",
        });

        SourceList.ItemsSource = null;
        SourceList.ItemsSource = _sources;
        UpdateCustomRootBox();
    }

    private void OnClearCustomRoot(object sender, RoutedEventArgs e)
    {
        var existing = CustomSource;
        if (existing is null)
            return;

        _sources.Remove(existing);
        SourceList.ItemsSource = null;
        SourceList.ItemsSource = _sources;
        UpdateCustomRootBox();
    }

    private static int CountSubfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            }).Count();
        }
        catch
        {
            return 0;
        }
    }

    // ==================== 热键捕获 ====================

    private void OnHotKeyBoxFocused(object sender, KeyboardFocusChangedEventArgs e)
        => HotKeyBox.SelectAll();

    /// <summary>
    /// 把按键直接翻译成 "Alt+." 这样的文本。
    /// 与 HotKeyParser 的符号→虚拟键码映射一一对应。
    /// </summary>
    private void OnHotKeyCapture(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // 按 Alt 组合键时 WPF 报的是 Key.System，真正的键在 SystemKey 里
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 只按住修饰键本身不算一个组合
        if (key is Key.LeftAlt or Key.RightAlt
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        var keyText = KeyToText(key);
        if (keyText is null)
            return;

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        if (parts.Count == 0)
        {
            // 没有修饰键会抢掉日常打字，拒绝
            HotKeyBox.Text = "（必须带 Ctrl / Alt / Shift / Win）";
            return;
        }

        parts.Add(keyText);
        HotKeyBox.Text = string.Join("+", parts);
    }

    private static string? KeyToText(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return key.ToString();

        if (key is >= Key.D0 and <= Key.D9)
            return ((int)(key - Key.D0)).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((int)(key - Key.NumPad0)).ToString();

        if (key is >= Key.F1 and <= Key.F24)
            return key.ToString();

        return key switch
        {
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.Oem1 => ";",
            Key.Oem2 => "/",
            Key.Oem3 => "`",
            Key.Oem4 => "[",
            Key.Oem5 => "\\",
            Key.Oem6 => "]",
            Key.Oem7 => "'",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.Space => " ",
            _ => null,
        };
    }

    // ==================== 维护 ====================

    private void OnClearIconCache(object sender, RoutedEventArgs e)
    {
        _icons.ClearDiskCache();
        IconsCleared = true;

        MessageBox.Show(this,
            "图标缓存已清空。关闭设置后会自动重新提取。",
            "应用启动器", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataRoot,
                UseShellExecute = true,
            });
        }
        catch
        {
            MessageBox.Show(this, $"打不开目录：\n{AppPaths.DataRoot}",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==================== 确认 / 取消 ====================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 正在捕获热键时 Esc 不该关掉整个对话框
        if (e.Key != Key.Escape || HotKeyBox.IsKeyboardFocusWithin)
            return;

        DialogResult = false;
        e.Handled = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var hotKey = HotKeyBox.Text.Trim();

        if (!HotKeyParser.TryParse(hotKey, out _, out _))
        {
            MessageBox.Show(this,
                $"「{hotKey}」不是一个有效的热键组合。\n\n"
                + "格式类似 Alt+. 、Ctrl+Alt+Space、Ctrl+Shift+F1，必须包含至少一个修饰键。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_sources.Count == 0)
        {
            MessageBox.Show(this,
                "至少要保留一个扫描来源，否则应用列表会是空的。\n\n"
                + "如果你只想手动添加应用，可以先随便留一个，之后在列表里右键移除不需要的。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store.Data.Scan.Sources = _sources;
        _store.Data.Scan.FilterNoise = FilterNoiseBox.IsChecked == true;
        _store.Data.Settings.HotKey = hotKey;
        _store.Data.Settings.HideAfterLaunch = HideAfterLaunchBox.IsChecked == true;

        HotKeyChanged = !string.Equals(hotKey, _originalHotKey, StringComparison.OrdinalIgnoreCase);

        DialogResult = true;
    }
}
