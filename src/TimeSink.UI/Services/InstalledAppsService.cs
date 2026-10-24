using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>
/// Win32 side of the Installed and Browse tabs: resolves <c>.lnk</c> files through <c>IShellLink</c>, lists packaged
/// apps from the <c>shell:AppsFolder</c> virtual folder, and fills publishers from file version info. The rules
/// (filtering, grouping, search) live in <see cref="InstalledCatalog"/>. Call from a pool thread.
/// </summary>
internal static class InstalledAppsService
{
    // ---- .lnk resolution ----------------------------------------------------------------------------------

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    /// <summary>The shortcut's target path, or null when it has none (a UWP or folder shortcut, a broken link).</summary>
    public static string? ResolveLnk(string lnkPath)
    {
        object? obj = null;
        try
        {
            obj = new ShellLink();
            ((IPersistFile)obj).Load(lnkPath, 0);
            var sb = new StringBuilder(520);
            ((IShellLinkW)obj).GetPath(sb, sb.Capacity, 0, 0);
            var path = Environment.ExpandEnvironmentVariables(sb.ToString());
            return path.Length == 0 ? null : path;
        }
        catch { return null; }
        finally { if (obj is not null) Marshal.ReleaseComObject(obj); }
    }

    // ---- Start Menu + packaged apps -----------------------------------------------------------------------

    public static IReadOnlyList<InstalledApp> Load()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
        };
        var shortcuts = StartMenuScanner.Scan(roots, ResolveLnk);
        var packaged = ListPackaged();
        var apps = InstalledCatalog.Build(shortcuts, packaged);
        return apps.Select(a => a.Aumid is null && a.Publisher is null ? a with { Publisher = Company(a.ExePath) } : a).ToList();
    }

    private static string? Company(string exe)
    {
        try
        {
            var c = FileVersionInfo.GetVersionInfo(exe).CompanyName;
            return string.IsNullOrWhiteSpace(c) ? null : c.Trim();
        }
        catch { return null; }
    }

    // IShellItem / IEnumShellItems, only what enumerating shell:AppsFolder needs.
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IEnumShellItems ppv);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetAttributes(uint mask, out uint attrs);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems e);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    private const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    private static IReadOnlyList<PackagedApp> ListPackaged()
    {
        var list = new List<PackagedApp>();
        try
        {
            var iidItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            if (SHCreateItemFromParsingName("shell:AppsFolder", 0, ref iidItem, out var folder) != 0) return list;
            var bhidEnum = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
            var iidEnum = new Guid("70629033-e363-4a28-a567-0db78006e6d7");
            if (folder.BindToHandler(0, ref bhidEnum, ref iidEnum, out var en) != 0) return list;
            while (en.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (item.GetDisplayName(SIGDN_PARENTRELATIVEPARSING, out var aumid) != 0) continue;
                    if (item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name) != 0) continue;
                    // packaged apps look like "Family_pubid!AppId"; classic apps are paths or GUIDs
                    if (aumid.Contains('!') && !aumid.Contains('\\') && !aumid.Contains('{')) list.Add(new PackagedApp(name, aumid));
                }
                finally { Marshal.ReleaseComObject(item); }
            }
        }
        catch { }
        return list;
    }
}
