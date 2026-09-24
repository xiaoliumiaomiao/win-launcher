using WinLauncher.Models;
using WinLauncher.Services;

namespace WinLauncher.ViewModels;

/// <summary>
/// 左侧栏里的一个分类。
///
/// 它只是个"标签"视图 —— 应用不装在分类里，而是各自记着自己属于哪些分类
/// （见 <see cref="AppEntry.CategoryIds"/>）。所以这里只需要维护一个计数。
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    /// <summary>合成的"全部"分类用的哨兵 Id，不会和真实分类撞车。</summary>
    public const string AllCategoryId = "__all__";

    public CategoryViewModel(Category model, bool isSynthetic = false)
    {
        Model = model;
        IsSynthetic = isSynthetic;
    }

    public Category Model { get; }

    /// <summary>合成的"全部"分类，不能改名也不能删，不属于磁盘上的数据。</summary>
    public bool IsSynthetic { get; }

    /// <summary>右键菜单里"重命名"是否可用。</summary>
    public bool CanEdit => !IsSynthetic;

    /// <summary>
    /// "删除"是否可用。内置分类不给删 —— 它的 Id 是固定的，
    /// 删掉下次启动会被分类器重新建出来，删了等于没删。
    /// 改名可以，Id 不变所以归类不受影响。
    /// </summary>
    public bool CanDelete => !IsSynthetic && !Model.IsBuiltIn;

    public string Name => Model.Name;

    /// <summary>侧栏统一使用开源 Fluent System Icons 字体的图标。</summary>
    public string IconGlyph => Model.Id switch
    {
        AllCategoryId => "\uF133",               // apps
        CategoryClassifier.Development => "\uF2EF", // code
        CategoryClassifier.Games => "\uE68A",       // games
        CategoryClassifier.Media => "\uE854",       // music note
        CategoryClassifier.Social => "\uF286",      // chat
        CategoryClassifier.Network => "\uE38D",     // cloud download
        CategoryClassifier.Office => "\uF378",      // document
        CategoryClassifier.Browser => "\uF45A",     // globe
        CategoryClassifier.Hardware => "\uE6E3",    // hard drive
        CategoryClassifier.WindowsAdmin => "\uF8B5",// window
        CategoryClassifier.SystemTools => "\uF82E", // toolbox
        CategoryClassifier.Other => "\uF133",       // apps
        _ => "\uF418",                              // folder
    };

    public bool IsBuiltIn => Model.IsBuiltIn;

    public int Count { get; private set; }

    public string CountLabel => Count == 0 ? "" : Count.ToString();

    public void SetCount(int count)
    {
        if (Count == count)
            return;

        Count = count;
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountLabel));
    }

    public void RefreshName()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsBuiltIn));
    }
}
