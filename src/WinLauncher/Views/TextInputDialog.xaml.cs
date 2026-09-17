using System.Windows;
using System.Windows.Input;

namespace WinLauncher.Views;

/// <summary>通用的单行文本输入弹窗（新建分类 / 重命名分类）。</summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string label, string initialValue)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        LabelText.Text = label;
        InputBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text.Trim();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        DialogResult = false;
        e.Handled = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show(this, "名称不能为空。", "应用启动器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
