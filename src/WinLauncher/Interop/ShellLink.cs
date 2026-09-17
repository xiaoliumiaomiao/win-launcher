using System.Runtime.InteropServices;
using System.Text;

namespace WinLauncher.Interop;

/// <summary>
/// 手写的 IShellLinkW / IPersistFile 互操作声明。
///
/// 为什么不用更省事的动态 COM（Type.GetTypeFromProgID("WScript.Shell")）：
/// WScript.Shell.CreateShortcut 在遇到死链时可能弹出模态对话框并挂住调用线程，
/// 而且拿不到控制权去传 SLR_NO_UI 这类"别弹窗、别搜索"的标志位。
/// 直接调 IShellLinkW 才能保证解析一个已卸载程序的快捷方式时静默返回。
///
/// ⚠️ COM 接口声明的方法顺序**必须**与 vtable 布局逐字对应，错一个位置后面全错位。
/// </summary>
internal static class ShellLinkInterop
{
    // IShellLink::Resolve / GetPath 的标志位
    public const uint SLR_NO_UI = 0x0001;
    public const uint SLR_NOSEARCH = 0x0010;
    public const uint SLR_NOTRACK = 0x0020;

    /// <summary>STGM_READ，传给 IPersistFile.Load，只读打开不修改快捷方式文件。</summary>
    public const uint STGM_READ = 0x00000000;

    /// <summary>
    /// 快捷方式 COM 类的 CLSID 声明。
    /// 用 Activator.CreateInstance(Type.GetTypeFromCLSID(...)) 创建，
    /// 避免声明 [ComImport] coclass 带来的可空性警告。
    /// </summary>
    public static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");

    internal static object CreateShellLinkInstance()
    {
        var type = Type.GetTypeFromCLSID(CLSID_ShellLink)
                   ?? throw new InvalidOperationException("系统未注册 ShellLink COM 组件");
        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException("无法创建 ShellLink COM 实例");
    }
}

/// <summary>
/// IShellLinkW。
///
/// ⚠️⚠️ 这个接口的 vtable 里**第一个方法就是 GetPath**，前面不能加
/// IPersist::GetClassID。
///
/// 虽然 C++ 头文件里写着 <c>IShellLinkW : public IPersist</c>，但真正决定 COM
/// 虚表布局的是 IDL —— IShellLinkW 直接继承 IUnknown，IPersistFile 是
/// coclass 另外实现的独立接口，需要单独 QueryInterface 拿。
///
/// 曾经在这里多声明了一个 GetClassID，结果后面**每个方法都错位一格**：
/// GetPath 实际调到了 GetIDList（把返回的 PIDL 指针当成 UTF-16 字符串读出来，
/// 得到 3-4 个乱码字符），Resolve 实际调到了 SetRelativePath（LPWSTR 收到
/// NULL，返回 E_INVALIDARG 0x80070057）。而且 GetPath 还返回 S_OK，
/// 没有任何异常，非常难查。改这个文件前请先跑 tools\diag-shelllink.ps1 对照验证。
/// </summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig]
    int GetPath(StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);

    void GetDescription(StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory(StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments(StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);

    void GetIconLocation(StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    /// <summary>
    /// 用 [PreserveSig] 保留 HRESULT：死链解析失败时返回非 0，
    /// 不加这个会直接抛 COMException，而我们只想忽略它继续拿 GetPath 的结果。
    /// </summary>
    [PreserveSig]
    int Resolve(IntPtr hwnd, uint fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

/// <summary>
/// IPersistFile，用于把 .lnk 文件加载进 IShellLinkW。
/// </summary>
[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    // ---- 来自 IPersist ----
    void GetClassID(out Guid pClassID);

    // ---- 来自 IPersistFile ----
    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName,
              [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}
