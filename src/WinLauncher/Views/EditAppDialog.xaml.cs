using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinLauncher.Models;
using WinLauncher.Services;
using WinLauncher.ViewModels;

namespace WinLauncher.Views;

/// <summary>
/// 编辑一个应用的名称和简介。
///
/// 用模态弹窗而不是"卡片就地变输入框"：就地编辑要处理卡片重排导致焦点丢失、
/// 失焦提交还是取消、以及列表回收时的状态丢失，代码路径多得多，
/// 而这个项目里简介是设置一次就不再改的东西，弹窗完全够用。
/// </summary>
public partial class EditAppDialog : Window
{
    private const int DescriptionMaxLength = 60;

    private readonly AppCardViewModel _card;
    private readonly LauncherData _data;

    public EditAppDialog(AppCardViewModel card, LauncherData data, bool focusName = false)
    {
        _card = card;
        _data = data;

        InitializeComponent();

        NameBox.Text = card.Name;
        DescriptionBox.Text = card.Description;
        TargetText.Text = card.Model.StableKey;

        DescriptionBox.TextChanged += (_, _) => UpdateCounter();
        UpdateCounter();

        Loaded += (_, _) =>
        {
            if (focusName)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
            else
            {
                DescriptionBox.Focus();
                DescriptionBox.SelectAll();
            }
        };
    }

    private void UpdateCounter()
    {
        var length = DescriptionBox.Text.Length;
        CounterText.Text = $"{length} / {DescriptionMaxLength}";
        CounterText.Foreground = length >= DescriptionMaxLength
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
    }

    private void OnAutoFill(object sender, RoutedEventArgs e)
    {
        var suggestion = EntryFactory.SuggestDescription(_card.Model.StableKey)
                         ?? EntryFactory.SuggestDescription(_card.Model.LaunchPath);

        if (string.IsNullOrWhiteSpace(suggestion))
        {
            MessageBox.Show(this,
                "这个程序没有可读取的描述信息，需要你自己写一句。",
                "应用启动器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DescriptionBox.Text = suggestion.Length > DescriptionMaxLength
            ? suggestion[..DescriptionMaxLength]
            : suggestion;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        e.Handled = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (!string.IsNullOrEmpty(name) && name != _card.Model.Name)
            LibraryService.SetName(_data, _card.Model, name);

        LibraryService.SetDescription(_data, _card.Model, DescriptionBox.Text.Trim());

        DialogResult = true;
    }
}
