using System.Reflection;
using System.Runtime.InteropServices;
using FastExplorer.Models;

namespace FastExplorer.Services;

// Restores/lists/permanently-deletes items in the Recycle Bin, used by both Ctrl+Z
// undo (TryRestore) and the Recycle Bin browser (Controls/RecycleBinView). Uses the
// same late-bound Shell.Application COM automation already validated in
// FileSystemServiceTests (rather than the strictly-typed IShellFolder/IContextMenu
// interop in ShellContextMenu.cs) since late binding can't corrupt memory on a
// vtable mismatch - a wrong property/verb name here just fails to find/act on the
// item. Every call re-enumerates the bin from scratch rather than caching COM item
// references across calls (ListItems returns a plain snapshot, not live handles) -
// simpler lifetime management, and the bin is never large enough for this to matter.
public static class RecycleBinService
{
    public static bool TryRestore(string originalFullPath) => InvokeVerbOnMatch(originalFullPath, "undelete");

    public static bool TryDeletePermanently(string originalFullPath) => InvokeVerbOnMatch(originalFullPath, "delete");

    public static IReadOnlyList<RecycleBinItemInfo> ListItems()
    {
        var results = new List<RecycleBinItemInfo>();

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return results;
        var shell = Activator.CreateInstance(shellType);
        if (shell is null) return results;

        try
        {
            var recycleBin = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { 10 });
            if (recycleBin is null) return results;

            var recycleBinType = recycleBin.GetType();
            var items = recycleBinType.InvokeMember("Items", BindingFlags.InvokeMethod, null, recycleBin, null);
            if (items is null) return results;

            var itemsType = items.GetType();
            var count = (int)itemsType.InvokeMember("Count", BindingFlags.GetProperty, null, items, null)!;

            for (var i = 0; i < count; i++)
            {
                var item = itemsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                if (item is null) continue;

                var itemType = item.GetType();
                var deletedFrom = itemType.InvokeMember(
                    "ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object?[] { "System.Recycle.DeletedFrom" }) as string;
                var name = itemType.InvokeMember("Name", BindingFlags.GetProperty, null, item, null) as string;
                if (string.IsNullOrEmpty(deletedFrom) || string.IsNullOrEmpty(name)) continue;

                var dateDeleted = itemType.InvokeMember(
                    "ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object?[] { "System.Recycle.DateDeleted" }) as DateTime? ?? default;
                var sizeRaw = itemType.InvokeMember(
                    "ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object?[] { "System.Size" });
                var size = sizeRaw switch { long l => l, int si => si, _ => 0L };

                results.Add(new RecycleBinItemInfo(name, deletedFrom, Path.Combine(deletedFrom, name), size, dateDeleted));
            }

            return results;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMemberException)
        {
            return results;
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    // SHEmptyRecycleBinW rather than iterating ListItems()+InvokeVerbOnMatch("delete")
    // per item - one native call, no per-item COM round-trips, and it's the same API
    // Explorer's own "Empty Recycle Bin" command uses.
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public static void EmptyRecycleBin()
    {
        SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
    }

    private static bool InvokeVerbOnMatch(string originalFullPath, string verb)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return false;

        var shell = Activator.CreateInstance(shellType);
        if (shell is null) return false;

        try
        {
            var recycleBin = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { 10 });
            if (recycleBin is null) return false;

            var recycleBinType = recycleBin.GetType();
            var items = recycleBinType.InvokeMember("Items", BindingFlags.InvokeMethod, null, recycleBin, null);
            if (items is null) return false;

            var itemsType = items.GetType();
            var count = (int)itemsType.InvokeMember("Count", BindingFlags.GetProperty, null, items, null)!;

            for (var i = 0; i < count; i++)
            {
                var item = itemsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                if (item is null) continue;

                var itemType = item.GetType();
                var deletedFrom = itemType.InvokeMember(
                    "ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object?[] { "System.Recycle.DeletedFrom" }) as string;
                var name = itemType.InvokeMember("Name", BindingFlags.GetProperty, null, item, null) as string;

                if (string.IsNullOrEmpty(deletedFrom) || string.IsNullOrEmpty(name)) continue;

                var candidateOriginalPath = Path.Combine(deletedFrom, name);
                if (!string.Equals(candidateOriginalPath, originalFullPath, StringComparison.OrdinalIgnoreCase)) continue;

                itemType.InvokeMember("InvokeVerb", BindingFlags.InvokeMethod, null, item, new object[] { verb });
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMemberException)
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }
}
