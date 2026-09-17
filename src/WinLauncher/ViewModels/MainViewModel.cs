using System.Collections.ObjectModel;
using WinLauncher.Models;
using WinLauncher.Services;

namespace WinLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DataStore _store;
    private readonly IconService _icons;

    /// <summary>
    /// 一个 AppEntry 对应一个固定卡片 VM。缓存复用而不是每次重建，
    /// 否则每敲一个搜索字符都会重建所有 VM 并触发图标重新请求（视觉上会闪）。
    /// </summary>
    private readonly Dictionary<AppEntry, AppCardViewModel> _cardCache =
        new(ReferenceEqualityComparer.Instance);

    private readonly CategoryViewModel _allCategory;

    private CategoryViewModel _selectedCategory;
    private string _searchText = "";

    public MainViewModel(DataStore store, IconService icons)
    {
        _store = store;
        _icons = icons;

        // "全部"是合成分类，不落盘
        _allCategory = new CategoryViewModel(
            new Category { Id = CategoryViewModel.AllCategoryId, Name = "全部" }, isSynthetic: true);
        _selectedCategory = _allCategory;

        RebuildCategories();
    }

    public LauncherData Data => _store.Data;

    public ObservableCollection<CategoryViewModel> Categories { get; } = [];

    public ObservableCollection<AppCardViewModel> VisibleApps { get; } = [];

    public CategoryViewModel SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetField(ref _selectedCategory, value))
                return;

            _store.Data.Settings.LastCategoryId = value.Model.Id;
            RefreshVisibleApps();
        }
    }

    /// <summary>有内容时跨所有分类搜索，不必先切到"全部"。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value))
                return;

            RefreshVisibleApps();
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    public bool IsEmpty => VisibleApps.Count == 0;

    public string EmptyTitle => IsSearching
        ? "没有匹配的应用"
        : SelectedCategory.IsSynthetic
            ? "还没有添加任何应用"
            : $"「{SelectedCategory.Name}」里还没有应用";

    public string EmptyHint => IsSearching
        ? "换个关键词试试"
        : "把桌面或开始菜单里的快捷方式直接拖到这里\n右键卡片可以写简介、改分类";

    public bool HasNoCategoriesAtAll => Data.Categories.Count == 0 && !IsSearching;

    /// <summary>当前选中的分类 Id，供右键菜单里的「归类」勾选状态和新建分类使用。</summary>
    public string SelectedCategoryId => SelectedCategory.Model.Id;

    // ---------------- 数据 → 界面 ----------------

    /// <summary>把磁盘数据重建到界面上。任何增删改之后都要调一次。</summary>
    public void RebuildCategories()
    {
        var previousId = _selectedCategory.Model.Id;

        Categories.Clear();
        _allCategory.SetCount(Data.Apps.Count);
        Categories.Add(_allCategory);

        foreach (var category in Data.Categories)
        {
            var count = CountIn(category.Id);

            // 空的内置分类不显示（比如没有硬件工具时那一行「硬件驱动」）。
            // 但用户自己建的分类即使空着也要显示 —— 否则他建完就找不到它了。
            if (count == 0 && category.IsBuiltIn)
                continue;

            var viewModel = new CategoryViewModel(category);
            viewModel.SetCount(count);
            Categories.Add(viewModel);
        }

        _selectedCategory = Categories.FirstOrDefault(c => c.Model.Id == previousId) ?? _allCategory;
        OnPropertyChanged(nameof(SelectedCategory));

        RefreshVisibleApps();
    }

    public void RefreshVisibleApps()
    {
        var source = IsSearching
            ? Data.Apps.Where(MatchesSearch)
            : AppsIn(SelectedCategory.Model.Id);

        var view = source.Select(GetOrCreateCard).ToList();

        VisibleApps.Clear();
        foreach (var card in view)
            VisibleApps.Add(card);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(HasNoCategoriesAtAll));

        _ = LoadIconsAsync(view);
    }

    /// <summary>顺序加载图标。IconService 内部是单线程 STA 队列，并发提交不会更快。</summary>
    private async Task LoadIconsAsync(IReadOnlyList<AppCardViewModel> cards)
    {
        foreach (var card in cards)
            await card.EnsureIconAsync();
    }

    public void RefreshMissingStates()
    {
        foreach (var card in _cardCache.Values)
            card.RefreshMissingState();
    }

    public async Task ReloadAllIconsAsync()
    {
        _icons.ClearDiskCache();
        _cardCache.Clear();
        RebuildCategories();
        await LoadIconsAsync(VisibleApps.ToList());
    }

    public AppCardViewModel GetOrCreateCard(AppEntry entry)
    {
        if (_cardCache.TryGetValue(entry, out var existing))
            return existing;

        var card = new AppCardViewModel(entry, _icons);
        _cardCache[entry] = card;
        return card;
    }

    /// <summary>某个分类下的应用（按名称排序，和 ScanService.SortAll 保持一致）。</summary>
    private IEnumerable<AppEntry> AppsIn(string categoryId)
        => string.Equals(categoryId, CategoryViewModel.AllCategoryId, StringComparison.Ordinal)
            ? Data.Apps
            : Data.Apps.Where(a => a.CategoryIds.Contains(categoryId, StringComparer.OrdinalIgnoreCase));

    public int CountIn(string categoryId) => AppsIn(categoryId).Count();

    private bool MatchesSearch(AppEntry entry)
    {
        var needle = _searchText.Trim();

        return entry.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || entry.Description.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || entry.LaunchPath.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
