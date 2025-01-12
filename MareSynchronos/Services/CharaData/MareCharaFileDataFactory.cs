using MareSynchronos.API.Data;
using MareSynchronos.FileCache;
using MareSynchronos.Services.CharaData.Models;

namespace MareSynchronos.Services.CharaData;

public sealed class MareCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public MareCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public MareCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new MareCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}