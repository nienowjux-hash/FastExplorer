using FastExplorer.Models;

namespace FastExplorer.Services;

public static class FileSystemService
{
    private static readonly TimeSpan DriveProbeTimeout = TimeSpan.FromSeconds(2);

    // DriveInfo.IsReady/VolumeLabel/TotalSize can each block for seconds on an empty
    // optical drive or an unreachable mapped network drive. Probing every drive in
    // parallel - each capped by its own timeout - means one bad drive costs at most
    // ~2 seconds instead of stalling (or serially delaying) the whole list.
    public static async Task GetDrivesAsync(Action<FileSystemItem> onDriveReady)
    {
        var tasks = DriveInfo.GetDrives().Select(drive => ProbeDriveAsync(drive, onDriveReady));
        await Task.WhenAll(tasks);
    }

    private static async Task ProbeDriveAsync(DriveInfo drive, Action<FileSystemItem> onDriveReady)
    {
        var probeTask = Task.Run(() => ProbeDriveBlocking(drive));
        var winner = await Task.WhenAny(probeTask, Task.Delay(DriveProbeTimeout));
        if (winner != probeTask) return; // didn't answer in time - skip it, don't wait longer

        var item = await probeTask;
        if (item is not null) onDriveReady(item);
    }

    private static FileSystemItem? ProbeDriveBlocking(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady) return null;

            var name = string.IsNullOrEmpty(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            long? total = null;
            long? free = null;
            try
            {
                total = drive.TotalSize;
                free = drive.TotalFreeSpace;
            }
            catch (IOException)
            {
                // A drive can report IsReady but still fail to answer a space query
                // (e.g. a card reader with a card removed mid-poll) - show it without usage info.
            }

            return new FileSystemItem
            {
                Name = name,
                FullPath = drive.Name,
                Kind = FileSystemItemKind.Drive,
                DateModified = default,
                DriveTotalBytes = total,
                DriveFreeBytes = free,
                IsNetworkDrive = drive.DriveType == DriveType.Network,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Enumerates a directory's immediate children. Uses EnumerationOptions with
    // IgnoreInaccessible so a single locked/permission-denied entry doesn't blow up
    // the whole listing - the fastest path to a responsive folder view.
    public static IEnumerable<FileSystemItem> EnumerateDirectory(string path, bool includeHidden = true)
    {
        var attributesToSkip = FileAttributes.System;
        if (!includeHidden) attributesToSkip |= FileAttributes.Hidden;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = attributesToSkip,
        };

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(path).EnumerateFileSystemInfos("*", options);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            FileSystemItem item;
            try
            {
                item = ToItem(entry);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            yield return item;
        }
    }

    // Raw FileAttributes bits not exposed as named members of System.IO.FileAttributes,
    // but set by the Cloud Files API (OneDrive, etc.) on placeholder/pinned entries.
    private const int FileAttributeRecallOnDataAccess = 0x00400000;
    private const int FileAttributePinned = 0x00080000;

    public static FileSystemItem ToItem(FileSystemInfo entry)
    {
        bool isDirectory = entry is DirectoryInfo;
        long size = 0;
        if (!isDirectory && entry is FileInfo fi)
        {
            size = fi.Length;
        }

        var rawAttributes = (int)entry.Attributes;

        return new FileSystemItem
        {
            Name = entry.Name,
            FullPath = entry.FullName,
            Kind = isDirectory ? FileSystemItemKind.Directory : FileSystemItemKind.File,
            SizeBytes = size,
            DateModified = entry.LastWriteTime,
            Extension = isDirectory ? string.Empty : entry.Extension,
            IsCloudPlaceholder = (rawAttributes & FileAttributeRecallOnDataAccess) != 0,
            IsCloudPinned = (rawAttributes & FileAttributePinned) != 0,
        };
    }

    public static void CreateFolder(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        Directory.CreateDirectory(path);
    }

    public static void Rename(FileSystemItem item, string newName)
    {
        var parent = Path.GetDirectoryName(item.FullPath)!;
        var newPath = Path.Combine(parent, newName);
        if (item.IsDirectory)
            Directory.Move(item.FullPath, newPath);
        else
            File.Move(item.FullPath, newPath);
    }

    public static void Delete(FileSystemItem item, bool permanently = false)
    {
        if (permanently)
        {
            if (item.IsDirectory)
                Directory.Delete(item.FullPath, recursive: true);
            else
                File.Delete(item.FullPath);
            return;
        }

        // Route through the Recycle Bin for safety (mirrors Explorer's default delete behavior).
        RecycleBinHelper.SendToRecycleBin(item.FullPath);
    }

    public static void Copy(FileSystemItem item, string destinationFolder, string? destinationName = null, bool overwrite = false)
    {
        var destPath = Path.Combine(destinationFolder, destinationName ?? item.Name);
        if (item.IsDirectory)
        {
            // "Replace" for a directory means wipe whatever is there and recopy,
            // not a file-by-file merge - simpler and avoids partial-merge edge cases.
            if (overwrite && Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true);
            CopyDirectory(item.FullPath, destPath);
        }
        else
        {
            File.Copy(item.FullPath, destPath, overwrite);
        }
    }

    public static void Move(FileSystemItem item, string destinationFolder, string? destinationName = null, bool overwrite = false)
    {
        var destPath = Path.Combine(destinationFolder, destinationName ?? item.Name);
        if (item.IsDirectory)
        {
            if (overwrite && Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true);
            Directory.Move(item.FullPath, destPath);
        }
        else
        {
            if (overwrite && File.Exists(destPath)) File.Delete(destPath);
            File.Move(item.FullPath, destPath, overwrite);
        }
    }

    public static bool Exists(string parentFolder, string name) =>
        File.Exists(Path.Combine(parentFolder, name)) || Directory.Exists(Path.Combine(parentFolder, name));

    public static string MakeUniqueName(string destinationFolder, string name)
    {
        var candidate = name;
        var ext = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var i = 2;
        while (Exists(destinationFolder, candidate))
        {
            candidate = $"{baseName} ({i++}){ext}";
        }
        return candidate;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
