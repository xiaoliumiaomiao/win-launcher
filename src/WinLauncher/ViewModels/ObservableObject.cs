using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinLauncher.ViewModels;

/// <summary>
/// 最小可用的 MVVM 基类。不引第三方框架 —— 这个项目的绑定需求很轻，
/// CommunityToolkit.Mvvm 带来的源生成器和额外依赖不划算。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
