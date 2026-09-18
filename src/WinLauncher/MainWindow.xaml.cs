using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinLauncher.Models;
using WinLauncher.Services;
using WinLauncher.ViewModels;
using WinLauncher.Views;

namespace WinLauncher;

public partial class MainWindow : Window
{
    private readonly DataStore _store;
    private readonly IconService _icons;
    private readonly HotKeyService _hotKeys;
    private readonly MainViewModel _viewModel;

    public MainWindow(DataStore store, IconService icons, HotKeyService hotKeys)
    {
        _store = store;
        _icons = icons;
        _hotKeys = hotKeys;

        InitializeComponent();

        _viewModel = new MainViewModel(store, icons);
        DataContext = _viewModel;

        // 提权会让拖放静默失效，必须主动提示
        if (ElevationGuard.IsElevated)
        {
            ElevationBanner.Visibility = Visibility.Visible;
            Diagnostics.Log("启动：检测到以管理员权限运行 —— 拖放功能将不可用");
        }

        Diagnostics.Log($"启动：数据目录={AppPaths.DataRoot} 被提权={ElevationGuard.IsElevated}");

        RestoreWindowPlacement();
        RegisterHotKey();

        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);
        // 拖到另一块缩放比例不同的显示器时，DWM 属性要重新施加，否则圆角/材质会错位
        DpiChanged += (_, _) => ThemeManager.ReapplyOnDpiChange(this);
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e) => RunStartupWork();

    private bool _startupWorkDone;

    /// <summary>
    /// 启动时要做的数据层工作（失效检测 + 同步）。
    ///
    /// 由 App 在创建窗口后直接调用，而不是等 <see cref="FrameworkElement.Loaded"/> ——
    /// 静默自启时窗口从不显示，Loaded 不会触发，扫描就永远不跑。
    /// 自己做一次幂等保护，两条路径都走到也只跑一次。
    /// </summary>
    public void RunStartupWork()
    {
        if (_startupWorkDone)
            return;

        _startupWorkDone = true;

        // 先用磁盘上的数据把界面画出来（首帧不等扫描），再去和扫描目录对账
        _viewModel.RefreshMissingStates();
        _ = RunScanAsync();
    }

    /// <summary>Alt+. 从任何地方把窗口叫到前台。</summary>
    public void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ==================== 窗口生命周期 ====================

    /// <summary>
    /// 恢复窗口位置和大小。
    ///
    /// 刻意不用 XAML 里的 WindowStartupLocation="CenterScreen"：
    /// 在 WindowStyle=None + WindowChrome 下它算出来的位置是偏的（实测在高 DPI 屏上
    /// 窗口会有一截跑到屏幕外），而且它不会检查窗口是否落在可见区域内。
    /// 自己算 + 夹回工作区，行为可预测。
    /// </summary>
    private void RestoreWindowPlacement()
    {
        var settings = _store.Data.Settings;

        var width = Math.Max(MinWidth, settings.WindowWidth);
        var height = Math.Max(MinHeight, settings.WindowHeight);

        Width = width;
        Height = height;

        // WorkArea 是 DIP 单位且已排除任务栏
        var work = SystemParameters.WorkArea;

        var left = settings.WindowLeft ?? work.Left + (work.Width - width) / 2;
        var top = settings.WindowTop ?? work.Top + (work.Height - height) / 2;

        // 上次可能是在一块已经拔掉的显示器上。夹回工作区，
        // 保证标题栏（也就是唯一的拖动把手）一定够得着。
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (bounds.Width < MinWidth || bounds.Height < MinHeight)
            return;

        var settings = _store.Data.Settings;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 注意：App 会在自己的 Closing 处理器里取消这次关闭并隐藏窗口，
        // 所以这里存盘不能等到窗口真的销毁
        SaveWindowPlacement();

        if (_store.Data.Settings.LastCategoryId is not null)
            _store.Data.Settings.LastCategoryId = _viewModel.SelectedCategory.Model.Id;

        try { _store.Save(); } catch { /* 存不下不影响关闭 */ }

        base.OnClosing(e);
    }

    private void RegisterHotKey()
    {
        var hotKey = _store.Data.Settings.HotKey;

        if (_hotKeys.TryRegister(hotKey))
            return;

        // 被别的软件占用了：不崩溃，提示用户去设置里换一个
        MessageBox.Show(
            $"全局热键「{hotKey}」注册失败，可能已被其它软件占用。\n\n" +
            "你可以在「设置」里换一个组合键。",
            "应用启动器", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==================== 标题栏 ====================

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => Close();   // 由 App 的 Closing 处理器转成 Hide

    private void OnRelaunchUnelevated(object sender, RoutedEventArgs e)
    {
        if (!ElevationGuard.TryRelaunchUnelevated())
        {
            MessageBox.Show(this,
                "无法自动以普通权限重启。\n\n请手动关闭本程序，然后直接双击桌面上的「应用启动器」图标打开。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Diagnostics.Log("用户请求以普通权限重启");
        Application.Current.Shutdown();
    }

    // ==================== 键盘 ====================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_viewModel.IsSearching)
                    _viewModel.SearchText = "";
                else
                    Hide();
                e.Handled = true;
                break;

            case Key.F5:
                RescanNow();
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                FocusSearchBox();
                e.Handled = true;
                break;
        }
    }

    // ==================== 拖拽添加 ====================

    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDragFeedback(e);

    private void OnDragOver(object sender, DragEventArgs e) => UpdateDragFeedback(e);

    /// <summary>开始菜单"所有应用"里拖出来的东西不是文件，而是虚拟 shell 文件夹的 ID 列表。</summary>
    private const string ShellIdListFormat = "Shell IDList Array";

    private void UpdateDragFeedback(DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        var hasShellIdList = !hasFiles && e.Data.GetDataPresent(ShellIdListFormat);
        var acceptable = hasFiles || hasShellIdList;

        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

        // ⚠️ 必须设 Handled，否则 WPF 会把 Effects 重置成 None，
        //    表现就是"能拖到窗口上，但松手没有任何反应"。
        e.Handled = true;

        if (hasShellIdList)
            DropTargetHint.Text = "这个来源给不出文件路径\n请改从资源管理器拖，或点左下「＋ 添加应用」";
        else
            DropTargetHint.Text = "松开即可添加到当前分类";

        DropOverlay.Visibility = acceptable ? Visibility.Visible : Visibility.Collapsed;

        Diagnostics.LogDrag(
            "DragOver",
            hasFiles,
            hasFiles && e.Data.GetData(DataFormats.FileDrop) is string[] f ? f.Length : 0,
            hasFiles && e.Data.GetData(DataFormats.FileDrop) is string[] f2 && f2.Length > 0 ? f2[0] : null,
            e.Effects.ToString());
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            Diagnostics.LogDrag("Drop 被忽略", false, 0, null, "-");
            return;
        }

        Diagnostics.LogDrag("Drop", true, paths.Length, paths[0], e.Effects.ToString());
        AddPaths(paths, ResolveDropTargetCategory());
    }

    /// <summary>
    /// 拖到左侧某个分类项上就落到那个分类；否则落到当前选中的分类。
    /// 拖到合成的"全部"上时，需要一个真实的分类来装，用「未分类」。
    /// </summary>
    private CategoryViewModel ResolveDropTargetCategory()
    {
        var selected = _viewModel.SelectedCategory;
        if (!selected.IsSynthetic)
            return selected;

        var existing = _store.Data.Categories.FirstOrDefault(c =>
            string.Equals(c.Name, "未分类", StringComparison.OrdinalIgnoreCase));

        var model = existing ?? LibraryService.AddCategory(_store.Data, "未分类");
        _viewModel.RebuildCategories();

        return _viewModel.Categories.First(c => ReferenceEquals(c.Model, model));
    }

    private void OnCategoryDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            return;

        // 找到鼠标底下的那个分类项
        var container = ItemsControl.ContainerFromElement(CategoryList, e.OriginalSource as DependencyObject)
                        as ListBoxItem;

        if (container?.DataContext is CategoryViewModel { IsSynthetic: false } category)
        {
            AddPaths(paths, category);
            return;
        }

        AddPaths(paths, ResolveDropTargetCategory());
    }

    private void AddPaths(IReadOnlyList<string> paths, CategoryViewModel category)
    {
        var result = LibraryService.AddFiles(_store.Data, category.Model, paths);
        _store.Save();
        _viewModel.RebuildCategories();

        if (!result.AnyFailed)
            return;

        var lines = new List<string>();
        if (result.Added > 0) lines.Add($"成功添加 {result.Added} 个");
        if (result.Duplicate > 0) lines.Add($"{result.Duplicate} 个已在列表中，已跳过");
        if (result.Unsupported > 0) lines.Add($"{result.Unsupported} 个不是支持的类型（支持 .lnk / .exe / .url / .bat / .cmd / .ps1，不支持文件夹）");
        if (result.Failed > 0) lines.Add($"{result.Failed} 个解析失败");

        MessageBox.Show(string.Join("\n", lines), "添加结果",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnBrowseAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要添加的程序",
            Multiselect = true,
            Filter = "程序与快捷方式|*.lnk;*.exe;*.url;*.bat;*.cmd;*.ps1|所有文件|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _viewModel.RebuildCategories();
        AddPaths(dialog.FileNames, ResolveDropTargetCategory());
    }

    // ==================== 卡片操作 ====================

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppCardViewModel card })
            return;

        var outcome = ProcessLauncher.Launch(card.Model, out var error);

        switch (outcome)
        {
            case LaunchOutcome.Started:
                if (_store.Data.Settings.HideAfterLaunch)
                    Hide();
                break;

            case LaunchOutcome.CancelledByUser:
                // 用户自己点了 UAC 的"取消"，不是错误，什么都不做
                break;

            case LaunchOutcome.NotFound:
                HandleMissingTarget(card);
                break;

            case LaunchOutcome.Failed:
                MessageBox.Show(
                    $"无法启动「{card.Name}」：\n\n{error}",
                    "应用启动器", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
    }

    private void HandleMissingTarget(AppCardViewModel card)
    {
        card.RefreshMissingState();

        var answer = MessageBox.Show(
            $"「{card.Name}」的目标已经找不到了：\n\n{card.Model.StableKey}\n\n" +
            "程序可能已被卸载或移动到别处。\n\n要从列表中移除它吗？",
            "应用启动器", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        LibraryService.RemoveEntry(_store.Data, card.Model);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void OnEditApp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppCardViewModel card })
            return;

        var dialog = new EditAppDialog(card, _store.Data) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void OnRenameApp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppCardViewModel card })
            return;

        var dialog = new EditAppDialog(card, _store.Data, focusName: true) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _store.Save();
        _viewModel.RebuildCategories();
    }

    /// <summary>
    /// 打开卡片右键菜单时，把「归类」子菜单填出来。
    ///
    /// 用可勾选项而不是"移动到分类/加入到分类"两个子菜单：
    /// 勾选状态直接说明这个应用当前在哪些分类里，勾上=加入、取消=移出，
    /// 一个菜单同时表达了"移动"和"多归属"两件事。
    /// </summary>
    private void OnCardMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        var card = (menu.PlacementTarget as FrameworkElement)?.DataContext as AppCardViewModel;
        if (card is null)
            return;

        var classify = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "归类");
        if (classify is null)
            return;

        classify.Items.Clear();

        foreach (var category in _viewModel.Categories)
        {
            if (category.IsSynthetic)
                continue;

            var categoryId = category.Model.Id;
            var item = new MenuItem
            {
                Header = category.Name,
                IsCheckable = true,
                IsChecked = card.Model.CategoryIds.Contains(categoryId, StringComparer.OrdinalIgnoreCase),
            };

            // 用闭包捕获 card 和 categoryId，而不是塞进 Tag ——
            // 动态创建的 MenuItem 不在卡片的可视树里，回溯 DataContext 拿不到东西
            var capturedItem = item;
            item.Click += (_, _) => ToggleCategory(card, categoryId, capturedItem.IsChecked);

            classify.Items.Add(item);
        }

        classify.Items.Add(new Separator());

        var create = new MenuItem { Header = "新建分类…" };
        create.Click += (_, _) => CreateCategoryForCard(card);
        classify.Items.Add(create);
    }

    private void ToggleCategory(AppCardViewModel card, string categoryId, bool shouldContain)
    {
        var ids = new List<string>(card.Model.CategoryIds);

        if (shouldContain)
        {
            if (!ids.Contains(categoryId, StringComparer.OrdinalIgnoreCase))
                ids.Add(categoryId);
        }
        else
        {
            ids.RemoveAll(id => string.Equals(id, categoryId, StringComparison.OrdinalIgnoreCase));
        }

        if (ids.Count == 0)
        {
            // 一个分类都不留的话应用会从所有列表里消失，界面上再也找不到它
            MessageBox.Show(this,
                "至少要保留一个分类。\n\n如果不想让它出现在当前分类里，"
                + "请先勾上另一个分类，再取消这一个。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LibraryService.SetCategories(_store.Data, card.Model, ids);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void CreateCategoryForCard(AppCardViewModel card)
    {
        var dialog = new TextInputDialog("新建分类", "分类名称", "") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var category = LibraryService.AddCategory(_store.Data, dialog.Value);

        var ids = new List<string>(card.Model.CategoryIds) { category.Id };
        LibraryService.SetCategories(_store.Data, card.Model, ids);

        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppCardViewModel card })
            return;

        var extra = card.Model.Source == AppSource.Scan
            ? "\n\n注意：它来自扫描目录，如果文件还在那里，下次同步会重新出现。"
            : "";

        var answer = MessageBox.Show(
            $"确定要从列表中移除「{card.Name}」吗？{extra}\n\n" +
            "（只会移除列表记录，不会删除任何程序文件）",
            "应用启动器", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        LibraryService.RemoveEntry(_store.Data, card.Model);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    // ==================== 分类操作 ====================

    private void OnAddCategory(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog("新建分类", "分类名称", "") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        LibraryService.AddCategory(_store.Data, dialog.Value);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void OnRenameCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CategoryViewModel category } || category.IsSynthetic)
            return;

        var dialog = new TextInputDialog("重命名分类", "分类名称", category.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        LibraryService.RenameCategory(category.Model, dialog.Value);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    private void OnDeleteCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CategoryViewModel category } || category.IsSynthetic)
            return;

        var answer = MessageBox.Show(
            $"确定要删除分类「{category.Name}」吗？\n\n" +
            $"里面的 {category.Count} 个应用会一并从列表中移除（程序文件不会被删除）。",
            "应用启动器", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        LibraryService.RemoveCategory(_store.Data, category.Model);
        _store.Save();
        _viewModel.RebuildCategories();
    }

    // ==================== 设置与同步 ====================

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_store, _icons) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _store.Save();

        if (dialog.HotKeyChanged)
            RegisterHotKey();

        if (dialog.IconsCleared)
        {
            _ = _viewModel.ReloadAllIconsAsync();
            return;
        }

        _ = RunScanAsync();
    }

    private void RescanNow()
    {
        _ = RunScanAsync();
        _viewModel.RefreshMissingStates();
    }

    /// <summary>
    /// 和扫描目录对账。
    ///
    /// 窗口先用手上已有的数据渲染出来，磁盘枚举放后台跑，回来后在 UI 线程上做对账
    /// —— 对账是纯内存计算，很快；这样即使扫描根在网络盘上，界面也不会卡住。
    /// </summary>
    private async Task RunScanAsync()
    {
        var config = _store.Data.Scan;

        var sources = config.Sources.Where(s => s.Enabled).ToList();
        if (sources.Count == 0)
        {
            Diagnostics.Log("同步跳过：没有任何启用中的扫描来源");
            _viewModel.RefreshMissingStates();
            return;
        }

        ScanBatch batch;
        try
        {
            batch = await Task.Run(() => ScanService.Enumerate(sources));
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"同步失败：{ex.Message}");
            return;
        }

        var isFirstSync = !config.HasAutoScanned;

        var outcome = ScanService.Apply(_store.Data, batch.Items);

        config.LastSyncUtc = DateTimeOffset.Now;
        config.HasAutoScanned = true;

        Diagnostics.Log(
            $"同步完成：来源 {sources.Count} 个 / 磁盘项 {batch.Items.Count} 个 → "
            + $"新增 {outcome.Added}，更新 {outcome.Updated}，失效 {outcome.Missing}，"
            + $"过滤噪声 {outcome.FilteredNoise}");

        LogCategoryDistribution();

        SaveQuietly();
        _viewModel.RebuildCategories();

        // 第一次自动扫描完成后给个交代 —— 否则用户打开就是一堆卡片，
        // 不知道这些是从哪来的、为什么有这么多
        if (isFirstSync && outcome.Added > 0)
            ShowFirstScanSummary(outcome, sources.Count);
    }

    /// <summary>
    /// 把分类分布写进日志。
    /// 调分类规则时不用开界面翻分类，看日志就能知道每个分类收了几个、有没有空的。
    /// </summary>
    private void LogCategoryDistribution()
    {
        var data = _store.Data;

        var summary = string.Join("  ", data.Categories
            .Select(c => new
            {
                c.Name,
                Count = data.Apps.Count(a =>
                    a.CategoryIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase)),
            })
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Name}:{x.Count}"));

        Diagnostics.Log($"分类分布（{data.Apps.Count} 个应用 / {data.Categories.Count} 个分类）：{summary}");

        var uncategorized = data.Apps.Count(a => a.CategoryIds.Count == 0);
        if (uncategorized > 0)
            Diagnostics.Log($"⚠ 有 {uncategorized} 个应用不属于任何分类，界面上会看不到它们");
    }

    private void ShowFirstScanSummary(ScanOutcome outcome, int sourceCount)
    {
        Diagnostics.Log($"首次自动扫描提示：新增 {outcome.Added}，分类 {_store.Data.Categories.Count}");

        ScanSummaryText.Text =
            $"已自动扫描 {sourceCount} 个位置，收进来 {outcome.Added} 个应用"
            + $"（{_store.Data.Categories.Count} 个分类），"
            + $"过滤掉 {outcome.FilteredNoise} 个卸载程序 / 帮助文档 / 官网链接。\n"
            + "不满意的可以右键移除；按 F5 随时重新同步；左下角「设置」里能调整扫描范围。";

        ScanSummaryBanner.Visibility = Visibility.Visible;
    }

    private void OnDismissScanSummary(object sender, RoutedEventArgs e)
        => ScanSummaryBanner.Visibility = Visibility.Collapsed;

    private void SaveQuietly()
    {
        try
        {
            _store.Save();
        }
        catch
        {
            // 存不下来不该让界面崩掉
        }
    }
}
