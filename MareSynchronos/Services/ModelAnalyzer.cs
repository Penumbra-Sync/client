using Dalamud.Plugin.Services;
using Lumina;
using Lumina.Data.Files;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class ModelAnalyzer
{
    private readonly ILogger<ModelAnalyzer> _logger;
    private readonly FileCacheManager _fileCacheManager;
    private readonly TriangleCalculationConfigService _configService;
    private readonly GameData _luminaGameData;

    public ModelAnalyzer(ILogger<ModelAnalyzer> logger, FileCacheManager fileCacheManager,
        TriangleCalculationConfigService configService, IDataManager gameData)
    {
        _logger = logger;
        _fileCacheManager = fileCacheManager;
        _configService = configService;
        _luminaGameData = new GameData(gameData.GameData.DataPath.FullName);
    }

    public Task<long> GetTrianglesFromGamePath(string gamePath)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(gamePath, out var cachedTris))
            return Task.FromResult(cachedTris);

        _logger.LogInformation("Detected Model File {path}, calculating Tris", gamePath);
        var file = _luminaGameData.GetFile<MdlFile>(gamePath);
        if (file == null)
            return Task.FromResult((long)0);

        if (file.FileHeader.LodCount <= 0)
            return Task.FromResult((long)0);
        var meshIdx = file.Lods[0].MeshIndex;
        var meshCnt = file.Lods[0].MeshCount;
        var tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;

        _logger.LogInformation("{filePath} => {tris} triangles", gamePath, tris);
        _configService.Current.TriangleDictionary[gamePath] = tris;
        _configService.Save();
        return Task.FromResult(tris);
    }

    public Task<long> GetTrianglesByHash(string hash)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(hash, out var cachedTris))
            return Task.FromResult(cachedTris);

        var path = _fileCacheManager.GetFileCacheByHash(hash);
        if (path == null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult((long)0);

        var filePath = path.ResolvedFilepath;

        _logger.LogInformation("Detected Model File {path}, calculating Tris", filePath);
        var file = _luminaGameData.GetFileFromDisk<MdlFile>(filePath);
        if (file.FileHeader.LodCount <= 0)
            return Task.FromResult((long)0);
        var meshIdx = file.Lods[0].MeshIndex;
        var meshCnt = file.Lods[0].MeshCount;
        var tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;

        _logger.LogInformation("{filePath} => {tris} triangles", filePath, tris);
        _configService.Current.TriangleDictionary[hash] = tris;
        _configService.Save();
        return Task.FromResult(tris);
    }
}
