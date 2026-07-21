using System.Runtime.InteropServices;

namespace FastExplorer.Services;

// P/Invoke wrapper around the same Win32 networking API Explorer's "Map Network Drive"
// wizard uses (mpr.dll), so a mapped drive shows up as a normal drive letter -
// FileSystemService.GetDrivesAsync picks it up with no extra wiring.
public static class NetworkDriveService
{
    private const int RESOURCE_TYPE_DISK = 0x1;
    private const uint CONNECT_UPDATE_PROFILE = 0x1;
    private const int NO_ERROR = 0;
    private const int ERROR_ALREADY_ASSIGNED = 85;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NETRESOURCE lpNetResource, string? lpPassword, string? lpUsername, uint dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string lpName, uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fForce);

    public static IReadOnlyList<string> GetAvailableDriveLetters()
    {
        var used = new HashSet<char>(
            DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));

        var available = new List<string>();
        for (var c = 'Z'; c >= 'D'; c--)
        {
            if (!used.Contains(c)) available.Add($"{c}:");
        }
        return available;
    }

    public static void MapDrive(string driveLetter, string remotePath, string? username, string? password, bool reconnectAtSignIn)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCE_TYPE_DISK,
            lpLocalName = driveLetter,
            lpRemoteName = remotePath,
        };

        var flags = reconnectAtSignIn ? CONNECT_UPDATE_PROFILE : 0;
        var result = WNetAddConnection2(ref resource, password, username, flags);
        if (result != NO_ERROR)
        {
            var message = result == ERROR_ALREADY_ASSIGNED
                ? $"Drive {driveLetter} is already in use."
                : $"Couldn't map {driveLetter} to {remotePath} (Win32 error {result}).";
            throw new IOException(message);
        }
    }

    public static void DisconnectDrive(string driveLetter)
    {
        var result = WNetCancelConnection2(driveLetter, 0, true);
        if (result != NO_ERROR)
        {
            throw new IOException($"Couldn't disconnect {driveLetter} (Win32 error {result}).");
        }
    }
}
