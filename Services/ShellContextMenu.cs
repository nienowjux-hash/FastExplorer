using System.Runtime.InteropServices;
using System.Text;
using FastExplorer.Models;

namespace FastExplorer.Services;

// Queries the Windows Shell's native IContextMenu for a single file/folder so
// third-party shell extensions (7-Zip, antivirus scanners, "Open Terminal here",
// etc.) show up in our own menu too, instead of us hand-rolling every possible
// integration. Deliberately scoped to a single selected item and to plain-text
// items (no owner-drawn icons/bitmaps) to keep the COM surface small - this is
// real native interop with third-party code we don't control, so every step is
// wrapped defensively and any failure just means "no extra items", never a crash.
public sealed class ShellContextMenu : IDisposable
{
    private const uint CmdFirst = 1;
    private const uint CmdLast = 0x7FFF;
    private const uint CmfNormal = 0x00000000;
    private const uint CmfExplore = 0x00000002;
    private const uint GcsVerbW = 0x00000004;
    private const uint MiimState = 0x00000001;
    private const uint MiimId = 0x00000002;
    private const uint MiimSubmenu = 0x00000004;
    private const uint MiimString = 0x00000040;
    private const uint MiimFtype = 0x00000100;
    private const uint MftSeparator = 0x00000800;
    private const uint MfsDisabled = 0x00000003;
    private const uint CmicMaskAsyncOk = 0x00100000;

    // Standard verbs we already implement ourselves - filtered out so the shell's
    // version (which would bypass our async/cancellable/conflict-resolved pipeline)
    // never shadows our own menu items. Anything without a recognized verb string
    // (which is most third-party extension entries) passes through untouched.
    private static readonly HashSet<string> SuppressedVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennew", "explore", "cut", "copy", "paste", "delete", "rename", "properties",
    };

    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu = new("000214e4-0000-0000-c000-000000000046");

    private IShellFolder? _desktop;
    private IShellFolder? _shellFolder;
    private IContextMenu? _contextMenu;
    private IntPtr _folderPidl;
    private IntPtr _itemPidl;
    private IntPtr _hMenu;

    private ShellContextMenu()
    {
    }

    public IReadOnlyList<ShellMenuEntry> Entries { get; private set; } = Array.Empty<ShellMenuEntry>();

    public static ShellContextMenu? TryCreate(string folderPath, string itemName, IntPtr ownerHwnd)
    {
        var menu = new ShellContextMenu();
        try
        {
            if (menu.Initialize(folderPath, itemName, ownerHwnd) && menu.Entries.Count > 0)
            {
                return menu;
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
        }

        menu.Dispose();
        return null;
    }

    private bool Initialize(string folderPath, string itemName, IntPtr ownerHwnd)
    {
        if (NativeMethods.SHGetDesktopFolder(out _desktop) != 0 || _desktop is null) return false;

        uint attrs = 0;
        uint eaten = 0;
        if (_desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, folderPath, ref eaten, out _folderPidl, ref attrs) != 0)
        {
            return false;
        }

        var shellFolderIid = IidShellFolder;
        if (_desktop.BindToObject(_folderPidl, IntPtr.Zero, ref shellFolderIid, out var shellFolderObj) != 0)
        {
            return false;
        }
        _shellFolder = (IShellFolder)shellFolderObj;

        attrs = 0;
        eaten = 0;
        if (_shellFolder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, itemName, ref eaten, out _itemPidl, ref attrs) != 0)
        {
            return false;
        }

        var contextMenuIid = IidContextMenu;
        if (_shellFolder.GetUIObjectOf(ownerHwnd, 1, ref _itemPidl, ref contextMenuIid, IntPtr.Zero, out var contextMenuObj) != 0)
        {
            return false;
        }
        _contextMenu = (IContextMenu)contextMenuObj;

        _hMenu = NativeMethods.CreatePopupMenu();
        if (_hMenu == IntPtr.Zero) return false;

        var hr = _contextMenu.QueryContextMenu(_hMenu, 0, CmdFirst, CmdLast, CmfNormal | CmfExplore);
        if (hr < 0) return false;

        Entries = ReadMenuItems(_hMenu);
        return true;
    }

    private List<ShellMenuEntry> ReadMenuItems(IntPtr hMenu)
    {
        var entries = new List<ShellMenuEntry>();
        var count = NativeMethods.GetMenuItemCount(hMenu);
        if (count < 0) return entries;

        const int bufferChars = 256;
        var buffer = Marshal.AllocHGlobal(bufferChars * 2);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var info = new MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                    fMask = MiimId | MiimSubmenu | MiimString | MiimFtype | MiimState,
                    dwTypeData = buffer,
                    cch = bufferChars,
                };

                if (!NativeMethods.GetMenuItemInfoW(hMenu, (uint)i, true, ref info)) continue;
                if ((info.fType & MftSeparator) != 0) continue;

                if (info.hSubMenu != IntPtr.Zero)
                {
                    var subItems = ReadMenuItems(info.hSubMenu);
                    if (subItems.Count == 0) continue;
                    var label = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                    entries.Add(new ShellMenuEntry { Label = StripAccelerator(label), SubItems = subItems });
                    continue;
                }

                if (info.wID == 0) continue;
                if (IsSuppressedVerb(info.wID)) continue;

                var itemLabel = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemLabel)) continue;

                entries.Add(new ShellMenuEntry
                {
                    Label = StripAccelerator(itemLabel),
                    CommandId = (int)info.wID,
                    IsEnabled = (info.fState & MfsDisabled) == 0,
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }

    private bool IsSuppressedVerb(uint id)
    {
        if (_contextMenu is null) return false;
        try
        {
            var sb = new StringBuilder(128);
            var hr = _contextMenu.GetCommandString((IntPtr)(id - CmdFirst), GcsVerbW, IntPtr.Zero, sb, (uint)sb.Capacity);
            return hr >= 0 && SuppressedVerbs.Contains(sb.ToString());
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string StripAccelerator(string label) => label.Replace("&", string.Empty);

    public void Invoke(ShellMenuEntry entry, IntPtr ownerHwnd)
    {
        if (_contextMenu is null || entry.CommandId <= 0) return;

        var info = new CMINVOKECOMMANDINFO
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
            fMask = CmicMaskAsyncOk,
            hwnd = ownerHwnd,
            lpVerb = (IntPtr)(entry.CommandId - (int)CmdFirst),
            nShow = 1, // SW_SHOWNORMAL
        };

        _contextMenu.InvokeCommand(ref info);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hMenu != IntPtr.Zero) NativeMethods.DestroyMenu(_hMenu);
        if (_itemPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(_itemPidl);
        if (_folderPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(_folderPidl);

        if (_contextMenu is not null) Marshal.ReleaseComObject(_contextMenu);
        if (_shellFolder is not null) Marshal.ReleaseComObject(_shellFolder);
        if (_desktop is not null) Marshal.ReleaseComObject(_desktop);

        _hMenu = IntPtr.Zero;
        _itemPidl = IntPtr.Zero;
        _folderPidl = IntPtr.Zero;
        _contextMenu = null;
        _shellFolder = null;
        _desktop = null;
    }
}

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

    [PreserveSig]
    int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

    [PreserveSig]
    int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [PreserveSig]
    int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    [PreserveSig]
    int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

    [PreserveSig]
    int GetUIObjectOf(IntPtr hwndOwner, uint cidl, ref IntPtr apidl, ref Guid riid, IntPtr rgfReserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [PreserveSig]
    int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

    [PreserveSig]
    int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

    [PreserveSig]
    int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, uint cchMax);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CMINVOKECOMMANDINFO
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    public IntPtr lpParameters;
    public IntPtr lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MENUITEMINFO
{
    public uint cbSize;
    public uint fMask;
    public uint fType;
    public uint fState;
    public uint wID;
    public IntPtr hSubMenu;
    public IntPtr hbmpChecked;
    public IntPtr hbmpUnchecked;
    public IntPtr dwItemData;
    public IntPtr dwTypeData;
    public uint cch;
    public IntPtr hbmpItem;
}

internal static class NativeMethods
{
    [DllImport("shell32.dll")]
    public static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMenuItemInfoW(IntPtr hMenu, uint uItem, bool fByPosition, ref MENUITEMINFO lpmii);
}
