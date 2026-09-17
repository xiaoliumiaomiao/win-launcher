<#
    对照测试：手写的 IShellLinkW COM 声明 vs 动态 WScript.Shell。
    用来定位"GetPath 返回乱码"是 COM 声明的问题还是别的问题。
#>
param([string]$Lnk = "$env:TEMP\launcher-test\开发工具\记事本.lnk")

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Diag
{
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
    {
        // 变体 B：不再声明 IPersist::GetClassID
        [PreserveSig] int GetPath(StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
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
        [PreserveSig] int Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    public static string Run(string lnkPath)
    {
        var sb = new StringBuilder();
        var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
        sb.AppendLine("CLSID 解析: " + (type != null));
        object obj = Activator.CreateInstance(type);
        sb.AppendLine("实例类型: " + obj.GetType().FullName);

        var link = (IShellLinkW)obj;
        sb.AppendLine("cast IShellLinkW: OK");
        var persist = (IPersistFile)obj;
        sb.AppendLine("cast IPersistFile: OK");

        try { persist.Load(lnkPath, 0); sb.AppendLine("Load: OK"); }
        catch (Exception ex) { sb.AppendLine("Load 抛异常: " + ex.Message); return sb.ToString(); }

        var hr = link.Resolve(IntPtr.Zero, 0x0001 | 0x0010 | 0x0020);
        sb.AppendLine("Resolve HRESULT: 0x" + hr.ToString("X8"));

        // 尺寸给足，并且打印 StringBuilder 的实际容量
        var buf = new StringBuilder(1024);
        sb.AppendLine("StringBuilder Capacity=" + buf.Capacity + " Length=" + buf.Length);
        var ghr = link.GetPath(buf, buf.Capacity, IntPtr.Zero, 0x0001);
        sb.AppendLine("GetPath HRESULT: 0x" + ghr.ToString("X8"));
        sb.AppendLine("GetPath 结果: '" + buf.ToString() + "'  (Length=" + buf.Length + ")");

        var args = new StringBuilder(1024);
        link.GetArguments(args, args.Capacity);
        sb.AppendLine("Arguments: '" + args.ToString() + "'");

        var wd = new StringBuilder(1024);
        link.GetWorkingDirectory(wd, wd.Capacity);
        sb.AppendLine("WorkingDirectory: '" + wd.ToString() + "'");

        var icon = new StringBuilder(1024);
        int iconIndex;
        link.GetIconLocation(icon, icon.Capacity, out iconIndex);
        sb.AppendLine("IconLocation: '" + icon.ToString() + "' index=" + iconIndex);

        var desc = new StringBuilder(1024);
        link.GetDescription(desc, desc.Capacity);
        sb.AppendLine("Description: '" + desc.ToString() + "'");

        return sb.ToString();
    }
}
'@

Write-Output "===== 被测快捷方式: $Lnk ====="
Write-Output ("存在: " + (Test-Path $Lnk))
Write-Output ""
Write-Output "----- A. 手写 IShellLinkW -----"
Write-Output ([Diag]::Run($Lnk))

Write-Output "----- B. 动态 WScript.Shell（对照组）-----"
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut($Lnk)
Write-Output ("TargetPath       : '" + $s.TargetPath + "'")
Write-Output ("Arguments        : '" + $s.Arguments + "'")
Write-Output ("WorkingDirectory : '" + $s.WorkingDirectory + "'")
Write-Output ("IconLocation     : '" + $s.IconLocation + "'")
Write-Output ("Description      : '" + $s.Description + "'")

