using System.Runtime.InteropServices;

namespace FastExplorer.Services;

// COM interop wrapper around IFileOperation (the modern replacement for the legacy
// SHFileOperationW) so deleting a file sends it to the Recycle Bin instead of a hard
// delete, without pulling in Windows Forms / Microsoft.VisualBasic as a dependency.
internal static class RecycleBinHelper
{
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOERRORUI = 0x0400;

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
    private class FileOperationComObject
    {
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems(IntPtr punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr fopsItem);
        void RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr fopsItem);
        void MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr fopsItem);
        void CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IntPtr fopsItem);
        void DeleteItems(IntPtr punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr fopsItem);
        void PerformOperations();
        void GetAnyOperationsAborted(out int pfAnyOperationsAborted);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    public static void SendToRecycleBin(string fullPath)
    {
        var fileOperation = (IFileOperation)new FileOperationComObject();
        try
        {
            fileOperation.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);

            var iid = IidShellItem;
            SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref iid, out var item);

            fileOperation.DeleteItem(item, IntPtr.Zero);
            fileOperation.PerformOperations();

            fileOperation.GetAnyOperationsAborted(out var aborted);
            if (aborted != 0)
            {
                throw new IOException($"Recycle Bin operation for '{fullPath}' was aborted.");
            }
        }
        finally
        {
            Marshal.ReleaseComObject(fileOperation);
        }
    }
}
