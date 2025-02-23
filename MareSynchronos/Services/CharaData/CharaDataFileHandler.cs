using Dalamud.Game.ClientState.Objects.SubKinds;
using K4os.Compression.LZ4.Legacy;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.FileCache;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.CharaData;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharaDataFileHandler : IDisposable
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManager _fileDownloadManager;
    private readonly FileUploadManager _fileUploadManager;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly ILogger<CharaDataFileHandler> _logger;
    private readonly MareCharaFileDataFactory _mareCharaFileDataFactory;
    private readonly PlayerDataFactory _playerDataFactory;
    private int _globalFileCounter = 0;

    public CharaDataFileHandler(ILogger<CharaDataFileHandler> logger, FileDownloadManagerFactory fileDownloadManagerFactory, FileUploadManager fileUploadManager, FileCacheManager fileCacheManager,
            DalamudUtilService dalamudUtilService, GameObjectHandlerFactory gameObjectHandlerFactory, PlayerDataFactory playerDataFactory)
    {
        _fileDownloadManager = fileDownloadManagerFactory.Create();
        _logger = logger;
        _fileUploadManager = fileUploadManager;
        _fileCacheManager = fileCacheManager;
        _dalamudUtilService = dalamudUtilService;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _playerDataFactory = playerDataFactory;
        _mareCharaFileDataFactory = new(fileCacheManager);
    }

    public void ComputeMissingFiles(CharaDataDownloadDto charaDataDownloadDto, out Dictionary<string, string> modPaths, out List<FileReplacementData> missingFiles)
    {
        modPaths = [];
        missingFiles = [];
        foreach (var file in charaDataDownloadDto.FileGamePaths)
        {
            var localCacheFile = _fileCacheManager.GetFileCacheByHash(file.HashOrFileSwap);
            if (localCacheFile == null)
            {
                var existingFile = missingFiles.Find(f => string.Equals(f.Hash, file.HashOrFileSwap, StringComparison.Ordinal));
                if (existingFile == null)
                {
                    missingFiles.Add(new FileReplacementData()
                    {
                        Hash = file.HashOrFileSwap,
                        GamePaths = [file.GamePath]
                    });
                }
                else
                {
                    existingFile.GamePaths = existingFile.GamePaths.Concat([file.GamePath]).ToArray();
                }
            }
            else
            {
                modPaths[file.GamePath] = localCacheFile.ResolvedFilepath;
            }
        }

        foreach (var swap in charaDataDownloadDto.FileSwaps)
        {
            modPaths[swap.GamePath] = swap.HashOrFileSwap;
        }
    }

    public async Task<CharacterData?> CreatePlayerData()
    {
        var chara = await _dalamudUtilService.GetPlayerCharacterAsync().ConfigureAwait(false);
        if (_dalamudUtilService.IsInGpose)
        {
            chara = (IPlayerCharacter?)(await _dalamudUtilService.GetGposeCharacterFromObjectTableByNameAsync(chara.Name.TextValue, _dalamudUtilService.IsInGpose).ConfigureAwait(false));
        }

        if (chara == null)
            return null;

        using var tempHandler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
                        () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(chara.ObjectIndex)?.Address ?? IntPtr.Zero, isWatched: false).ConfigureAwait(false);
        PlayerData.Data.CharacterData newCdata = new();
        var fragment = await _playerDataFactory.BuildCharacterData(tempHandler, CancellationToken.None).ConfigureAwait(false);
        newCdata.SetFragment(ObjectKind.Player, fragment);
        if (newCdata.FileReplacements.TryGetValue(ObjectKind.Player, out var playerData) && playerData != null)
        {
            foreach (var data in playerData.Select(g => g.GamePaths))
            {
                data.RemoveWhere(g => g.EndsWith(".pap", StringComparison.OrdinalIgnoreCase)
                    || g.EndsWith(".tmb", StringComparison.OrdinalIgnoreCase)
                    || g.EndsWith(".scd", StringComparison.OrdinalIgnoreCase)
                    || (g.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase))
                    || (g.EndsWith(".atex", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/weapon/", StringComparison.OrdinalIgnoreCase)
                        && !g.Contains("/equipment/", StringComparison.OrdinalIgnoreCase)));
            }

            playerData.RemoveWhere(g => g.GamePaths.Count == 0);
        }

        return newCdata.ToAPI();
    }

    public void Dispose()
    {
        _fileDownloadManager.Dispose();
    }

    public async Task DownloadFilesAsync(GameObjectHandler tempHandler, List<FileReplacementData> missingFiles, Dictionary<string, string> modPaths, CancellationToken token)
    {
        await _fileDownloadManager.InitiateDownloadList(tempHandler, missingFiles, token).ConfigureAwait(false);
        await _fileDownloadManager.DownloadFiles(tempHandler, missingFiles, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        foreach (var file in missingFiles.SelectMany(m => m.GamePaths, (FileEntry, GamePath) => (FileEntry.Hash, GamePath)))
        {
            var localFile = _fileCacheManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath;
            if (localFile == null)
            {
                throw new FileNotFoundException("File not found locally.");
            }
            modPaths[file.GamePath] = localFile;
        }
    }

    public Task<(MareCharaFileHeader loadedCharaFile, long expectedLength)> LoadCharaFileHeader(string filePath)
    {
        try
        {
            using var unwrapped = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            var loadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);

            _logger.LogInformation("Read Mare Chara File");
            _logger.LogInformation("Version: {ver}", (loadedCharaFile?.Version ?? -1));
            long expectedLength = 0;
            if (loadedCharaFile != null)
            {
                _logger.LogTrace("Data");
                foreach (var item in loadedCharaFile.CharaFileData.FileSwaps)
                {
                    foreach (var gamePath in item.GamePaths)
                    {
                        _logger.LogTrace("Swap: {gamePath} => {fileSwapPath}", gamePath, item.FileSwapPath);
                    }
                }

                var itemNr = 0;
                foreach (var item in loadedCharaFile.CharaFileData.Files)
                {
                    itemNr++;
                    expectedLength += item.Length;
                    foreach (var gamePath in item.GamePaths)
                    {
                        _logger.LogTrace("File {itemNr}: {gamePath} = {len}", itemNr, gamePath, item.Length.ToByteString());
                    }
                }

                _logger.LogInformation("Expected length: {expected}", expectedLength.ToByteString());
            }
            else
            {
                throw new InvalidOperationException("MCDF Header was null");
            }
            return Task.FromResult((loadedCharaFile, expectedLength));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse MCDF header of file {file}", filePath);
            throw;
        }
    }

    public Dictionary<string, string> McdfExtractFiles(MareCharaFileHeader? charaFileHeader, long expectedLength, List<string> extractedFiles)
    {
        if (charaFileHeader == null) return [];

        using var lz4Stream = new LZ4Stream(File.OpenRead(charaFileHeader.FilePath), LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
        using var reader = new BinaryReader(lz4Stream);
        MareCharaFileHeader.AdvanceReaderToData(reader);

        long totalRead = 0;
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_fileCacheManager.CacheFolder, "mare_" + _globalFileCounter++ + ".tmp");
            extractedFiles.Add(fileName);
            var length = fileData.Length;
            var bufferSize = length;
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            _logger.LogTrace("Reading {length} of {fileName}", length.ToByteString(), fileName);
            var buffer = reader.ReadBytes(bufferSize);
            wr.Write(buffer);
            wr.Flush();
            wr.Close();
            if (buffer.Length == 0) throw new EndOfStreamException("Unexpected EOF");
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = fileName;
                _logger.LogTrace("{path} => {fileName} [{hash}]", path, fileName, fileData.Hash);
            }
            totalRead += length;
            _logger.LogTrace("Read {read}/{expected} bytes", totalRead.ToByteString(), expectedLength.ToByteString());
        }

        return gamePathToFilePath;
    }

    public async Task UpdateCharaDataAsync(CharaDataExtendedUpdateDto updateDto)
    {
        var data = await CreatePlayerData().ConfigureAwait(false);

        if (data != null)
        {
            var hasGlamourerData = data.GlamourerData.TryGetValue(ObjectKind.Player, out var playerDataString);
            if (!hasGlamourerData) updateDto.GlamourerData = null;
            else updateDto.GlamourerData = playerDataString;

            var hasCustomizeData = data.CustomizePlusData.TryGetValue(ObjectKind.Player, out var customizeDataString);
            if (!hasCustomizeData) updateDto.CustomizeData = null;
            else updateDto.CustomizeData = customizeDataString;

            updateDto.ManipulationData = data.ManipulationData;

            var hasFiles = data.FileReplacements.TryGetValue(ObjectKind.Player, out var fileReplacements);
            if (!hasFiles)
            {
                updateDto.FileGamePaths = [];
                updateDto.FileSwaps = [];
            }
            else
            {
                updateDto.FileGamePaths = [.. fileReplacements!.Where(u => string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.Hash, path))];
                updateDto.FileSwaps = [.. fileReplacements!.Where(u => !string.IsNullOrEmpty(u.FileSwapPath)).SelectMany(u => u.GamePaths, (file, path) => new GamePathEntry(file.FileSwapPath, path))];
            }
        }
    }

    internal async Task SaveCharaFileAsync(string description, string filePath)
    {
        var tempFilePath = filePath + ".tmp";

        try
        {
            var data = await CreatePlayerData().ConfigureAwait(false);
            if (data == null) return;

            var mareCharaFileData = _mareCharaFileDataFactory.Create(description, data);
            MareCharaFileHeader output = new(MareCharaFileHeader.CurrentVersion, mareCharaFileData);

            using var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4);
            output.WriteToStream(writer);

            foreach (var item in output.CharaFileData.Files)
            {
                var file = _fileCacheManager.GetFileCacheByHash(item.Hash)!;
                _logger.LogDebug("Saving to MCDF: {hash}:{file}", item.Hash, file.ResolvedFilepath);
                _logger.LogDebug("\tAssociated GamePaths:");
                foreach (var path in item.GamePaths)
                {
                    _logger.LogDebug("\t{path}", path);
                }

                var fsRead = File.OpenRead(file.ResolvedFilepath);
                await using (fsRead.ConfigureAwait(false))
                {
                    using var br = new BinaryReader(fsRead);
                    byte[] buffer = new byte[item.Length];
                    br.Read(buffer, 0, item.Length);
                    writer.Write(buffer);
                }
            }
            writer.Flush();
            await lz4.FlushAsync().ConfigureAwait(false);
            await fs.FlushAsync().ConfigureAwait(false);
            fs.Close();
            File.Move(tempFilePath, filePath, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failure Saving Mare Chara File, deleting output");
            File.Delete(tempFilePath);
        }
    }

    internal async Task<List<string>> UploadFiles(List<string> fileList, ValueProgress<string> uploadProgress, CancellationToken token)
    {
        return await _fileUploadManager.UploadFiles(fileList, uploadProgress, token).ConfigureAwait(false);
    }
}
