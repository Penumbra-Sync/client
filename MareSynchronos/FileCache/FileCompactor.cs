using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace MareSynchronos.FileCache;

public sealed class FileCompactor
{
    public const uint FSCTL_DELETE_EXTERNAL_BACKING = 0x90314U;
    public const ulong WOF_PROVIDER_FILE = 2UL;

    private readonly Dictionary<string, int> _clusterSizes;

    private readonly WOF_FILE_COMPRESSION_INFO_V1 _efInfo;
    private readonly ILogger<FileCompactor> _logger;

    private readonly MareConfigService _mareConfigService;
    private readonly DalamudUtilService _dalamudUtilService;

    public FileCompactor(ILogger<FileCompactor> logger, MareConfigService mareConfigService, DalamudUtilService dalamudUtilService)
    {
        _clusterSizes = new(StringComparer.Ordinal);
        _logger = logger;
        _mareConfigService = mareConfigService;
        _dalamudUtilService = dalamudUtilService;
        _efInfo = new WOF_FILE_COMPRESSION_INFO_V1
        {
            Algorithm = CompressionAlgorithm.XPRESS8K,
            Flags = 0
        };
    }

    private enum CompressionAlgorithm
    {
        NO_COMPRESSION = -2,
        LZNT1 = -1,
        XPRESS4K = 0,
        LZX = 1,
        XPRESS8K = 2,
        XPRESS16K = 3
    }

    public bool MassCompactRunning { get; private set; } = false;

    public string Progress { get; private set; } = string.Empty;

    public void CompactStorage(bool compress)
    {
        MassCompactRunning = true;

        int currentFile = 1;
        var allFiles = Directory.EnumerateFiles(_mareConfigService.Current.CacheFolder).ToList();
        int allFilesCount = allFiles.Count;
        foreach (var file in allFiles)
        {
            Progress = $"{currentFile}/{allFilesCount}";
            if (compress)
                CompactFile(file);
            else
                DecompressFile(file);
            currentFile++;
        }

        MassCompactRunning = false;
    }

    public long GetFileSizeOnDisk(FileInfo fileInfo, bool? isNTFS = null)
    {
        bool ntfs = isNTFS ?? string.Equals(new DriveInfo(fileInfo.Directory!.Root.FullName).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);

        if (_dalamudUtilService.IsWine || !ntfs) return fileInfo.Length;

        var clusterSize = GetClusterSize(fileInfo);
        if (clusterSize == -1) return fileInfo.Length;
        var losize = GetCompressedFileSizeW(fileInfo.FullName, out uint hosize);
        var size = (long)hosize << 32 | losize;
        return ((size + clusterSize - 1) / clusterSize) * clusterSize;
    }

    public async Task WriteAllBytesAsync(string filePath, byte[] decompressedFile, CancellationToken token)
    {
        await File.WriteAllBytesAsync(filePath, decompressedFile, token).ConfigureAwait(false);

        if (_dalamudUtilService.IsWine || !_mareConfigService.Current.UseCompactor)
        {
            return;
        }

        CompactFile(filePath);
    }

    [DllImport("kernel32.dll")]
    private static extern int DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out IntPtr lpBytesReturned, out IntPtr lpOverlapped);

    [DllImport("kernel32.dll")]
    private static extern uint GetCompressedFileSizeW([In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
                                              [Out, MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

    [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
    private static extern int GetDiskFreeSpaceW([In, MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName,
           out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters,
           out uint lpTotalNumberOfClusters);

    [DllImport("WoFUtil.dll")]
    private static extern int WofIsExternalFile([MarshalAs(UnmanagedType.LPWStr)] string Filepath, out int IsExternalFile, out uint Provider, out WOF_FILE_COMPRESSION_INFO_V1 Info, ref uint BufferLength);

    [DllImport("WofUtil.dll")]
    private static extern int WofSetFileDataLocation(IntPtr FileHandle, ulong Provider, IntPtr ExternalFileInfo, ulong Length);

    private void CompactFile(string filePath)
    {
        var fs = new DriveInfo(new FileInfo(filePath).Directory!.Root.FullName);
        bool isNTFS = string.Equals(fs.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        if (!isNTFS)
        {
            _logger.LogWarning("Drive for file {file} is not NTFS", filePath);
            return;
        }

        var fi = new FileInfo(filePath);
        var oldSize = fi.Length;
        var clusterSize = GetClusterSize(fi);

        if (oldSize < Math.Max(clusterSize, 8 * 1024))
        {
            _logger.LogDebug("File {file} is smaller than cluster size ({size}), ignoring", filePath, clusterSize);
            return;
        }

        if (!IsCompactedFile(filePath))
        {
            _logger.LogDebug("Compacting file to XPRESS8K: {file}", filePath);

            WOFCompressFile(filePath);

            var newSize = GetFileSizeOnDisk(fi);

            _logger.LogDebug("Compressed {file} from {orig}b to {comp}b", filePath, oldSize, newSize);
        }
        else
        {
            _logger.LogDebug("File {file} already compressed", filePath);
        }
    }

    private void DecompressFile(string path)
    {
        _logger.LogDebug("Removing compression from {file}", path);
        try
        {
            using (var fs = new FileStream(path, FileMode.Open))
            {
#pragma warning disable S3869 // "SafeHandle.DangerousGetHandle" should not be called
                var hDevice = fs.SafeFileHandle.DangerousGetHandle();
#pragma warning restore S3869 // "SafeHandle.DangerousGetHandle" should not be called
                _ = DeviceIoControl(hDevice, FSCTL_DELETE_EXTERNAL_BACKING, nint.Zero, 0, nint.Zero, 0, out _, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error decompressing file {path}", path);
        }
    }

    private int GetClusterSize(FileInfo fi)
    {
        if (!fi.Exists) return -1;
        var root = fi.Directory?.Root.FullName.ToLower() ?? string.Empty;
        if (string.IsNullOrEmpty(root)) return -1;
        if (_clusterSizes.TryGetValue(root, out int value)) return value;
        _logger.LogDebug("Getting Cluster Size for {path}, root {root}", fi.FullName, root);
        int result = GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _);
        if (result == 0) return -1;
        _clusterSizes[root] = (int)(sectorsPerCluster * bytesPerSector);
        _logger.LogDebug("Determined Cluster Size for root {root}: {cluster}", root, _clusterSizes[root]);
        return _clusterSizes[root];
    }

    private static bool IsCompactedFile(string filePath)
    {
        uint buf = 8;
        _ = WofIsExternalFile(filePath, out int isExtFile, out uint _, out var info, ref buf);
        if (isExtFile == 0) return false;
        return info.Algorithm == CompressionAlgorithm.XPRESS8K;
    }

    private void WOFCompressFile(string path)
    {
        var efInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_efInfo));
        Marshal.StructureToPtr(_efInfo, efInfoPtr, fDeleteOld: true);
        ulong length = (ulong)Marshal.SizeOf(_efInfo);
        try
        {
            using (var fs = new FileStream(path, FileMode.Open))
            {
#pragma warning disable S3869 // "SafeHandle.DangerousGetHandle" should not be called
                var hFile = fs.SafeFileHandle.DangerousGetHandle();
#pragma warning restore S3869 // "SafeHandle.DangerousGetHandle" should not be called
                if (fs.SafeFileHandle.IsInvalid)
                {
                    _logger.LogWarning("Invalid file handle to {file}", path);
                }
                else
                {
                    var ret = WofSetFileDataLocation(hFile, WOF_PROVIDER_FILE, efInfoPtr, length);
                    if (!(ret == 0 || ret == unchecked((int)0x80070158)))
                    {
                        _logger.LogWarning("Failed to compact {file}: {ret}", path, ret.ToString("X"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error compacting file {path}", path);
        }
        finally
        {
            Marshal.FreeHGlobal(efInfoPtr);
        }
    }

    private struct WOF_FILE_COMPRESSION_INFO_V1
    {
        public CompressionAlgorithm Algorithm;
        public ulong Flags;
    }
}