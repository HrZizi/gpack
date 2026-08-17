namespace GPack.Installer.Core;

public class DiskSpaceInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public long AvailableFreeSpaceBytes { get; set; }
    public long TotalDiskSizeBytes { get; set; }
    public long RequiredSpaceBytes { get; set; }
    public bool HasSufficientSpace => AvailableFreeSpaceBytes >= RequiredSpaceBytes;
}

public static class DiskSpaceChecker
{
    public static DiskSpaceInfo CheckSpace(string targetPath, long requiredBytes)
    {
        string fullPath = Path.GetFullPath(targetPath);
        string root = Path.GetPathRoot(fullPath) ?? "C:\\";

        try
        {
            var driveInfo = new DriveInfo(root);
            return new DiskSpaceInfo
            {
                DriveLetter = driveInfo.Name,
                AvailableFreeSpaceBytes = driveInfo.AvailableFreeSpace,
                TotalDiskSizeBytes = driveInfo.TotalSize,
                RequiredSpaceBytes = requiredBytes
            };
        }
        catch
        {
            // Fallback if drive querying fails (e.g. network share or virtual drive)
            return new DiskSpaceInfo
            {
                DriveLetter = root,
                AvailableFreeSpaceBytes = long.MaxValue,
                TotalDiskSizeBytes = long.MaxValue,
                RequiredSpaceBytes = requiredBytes
            };
        }
    }
}
