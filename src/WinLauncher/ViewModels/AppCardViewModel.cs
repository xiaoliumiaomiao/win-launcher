using System.Windows.Media;
using WinLauncher.Models;
using WinLauncher.Services;

namespace WinLauncher.ViewModels;

/// <summary>一张应用卡片。</summary>
public sealed class AppCardViewModel : ObservableObject
{
    private readonly IconService _icons;
    private ImageSource? _icon;
    private bool _isLoadingIcon;

    public AppCardViewModel(AppEntry model, IconService icons)
    {
        Model = model;
        _icons = icons;
    }

    public AppEntry Model { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
                return;

            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value)
                return;

            Model.Description = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDescription));
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Model.Description);

    /// <summary>目标不存在（程序被卸载/移动）时置灰，而不是点了没反应。</summary>
    public bool IsMissing => Model.IsMissing;

    public string MissingTooltip =>
        IsMissing
            ? $"目标已不存在：\n{Model.StableKey}\n\n右键可将其移除"
            : Model.StableKey;

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetField(ref _icon, value);
    }

    /// <summary>首屏先出占位，图标提取完再补上，避免几十张卡片同时提取时界面卡住。</summary>
    public async Task EnsureIconAsync()
    {
        if (_icon is not null || _isLoadingIcon)
            return;

        _isLoadingIcon = true;
        try
        {
            Icon = await _icons.GetAsync(Model.LaunchPath);
        }
        finally
        {
            _isLoadingIcon = false;
        }
    }

    /// <summary>重新探测目标是否还在（启动时 / 同步完成后调用）。</summary>
    public void RefreshMissingState()
    {
        var wasMissing = Model.MissingSince is not null;
        var alive = EntryFactory.IsTargetAlive(Model);

        if (alive)
            Model.MissingSince = null;
        else
            Model.MissingSince ??= DateTimeOffset.Now;   // 保留最早失效时刻

        if (wasMissing != (Model.MissingSince is not null))
        {
            OnPropertyChanged(nameof(IsMissing));
            OnPropertyChanged(nameof(MissingTooltip));
        }
    }
}
