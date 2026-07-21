using System.Reflection;
using System.Runtime.InteropServices;

namespace FastExplorer.Services;

// Restores a specific deleted item from the Recycle Bin, for Ctrl+Z support.
// Uses the same late-bound Shell.Application COM automation already validated in
// FileSystemServiceTests (rather than the strictly-typed IShellFolder/IContextMenu
// interop in ShellContextMenu.cs) since late binding can't corrupt memory on a
// vtable mismatch - a wrong property name here just fails to find the item.
public static class RecycleBinService
{
    public static bool TryRestore(string originalFullPath)
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

                itemType.InvokeMember("InvokeVerb", BindingFlags.InvokeMethod, null, item, new object[] { "undelete" });
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
