using System.Text;
using System.Windows;

namespace WinLauncher.Views;

public partial class LicensesDialog : Window
{
    public LicensesDialog()
    {
        InitializeComponent();

        var license = ReadEmbedded("LICENSE-FluentSystemIcons.txt");
        var notice = ReadEmbedded("NOTICE-FluentSystemIcons.txt");
        if (license is null || notice is null)
        {
            LicenseText.Text = "无法读取内嵌的许可文本。";
            return;
        }

        LicenseText.Text = license + "\n\nTHIRD-PARTY NOTICE\n\n" + notice;
    }

    private static string? ReadEmbedded(string fileName)
    {
        var uri = new Uri($"pack://application:,,,/Assets/Fonts/{fileName}");
        using var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
