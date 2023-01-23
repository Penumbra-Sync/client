using MareSynchronos.API;
using MareSynchronos.FileCache;

namespace MareSynchronos.Export;

internal class MareCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public MareCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public MareCharaFileData Create(string description, CharacterCacheDto characterCacheDto)
    {
        return new MareCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}
