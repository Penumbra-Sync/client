#nullable disable


namespace MareSynchronos.FileCacheDB
{

    public class FileCache
    {
        private FileCacheEntity entity;
        public string Filepath { get; private set; }
        public string Hash { get; private set; }
        public string OriginalFilepath => entity.Filepath;
        public string OriginalHash => entity.Hash;
        public long LastModifiedDateTicks => long.Parse(entity.LastModifiedDate);

        public FileCache(FileCacheEntity entity)
        {
            this.entity = entity;
        }

        public void SetResolvedFilePath(string filePath)
        {
            Filepath = filePath.ToLowerInvariant().Replace("\\\\", "\\");
        }

        public void SetHash(string hash)
        {
            Hash = hash;
        }

        public void UpdateFileCache(FileCacheEntity entity)
        {
            this.entity = entity;
        }
    }
}
