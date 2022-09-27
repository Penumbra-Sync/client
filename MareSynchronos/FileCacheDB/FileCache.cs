#nullable disable


using System;

namespace MareSynchronos.FileCacheDB
{

    public class FileCache
    {
        private FileCacheEntity entity;
        public string Filepath { get; private set; }
        public string Hash { get; private set; }
        private string originalFilePathNoEntity = string.Empty;
        private string originalHashNoEntity = string.Empty;
        private string originalModifiedDate = string.Empty;
        public string OriginalFilepath => entity == null ? originalFilePathNoEntity : entity.Filepath;
        public string OriginalHash => entity == null ? originalHashNoEntity : entity.Hash;
        public long LastModifiedDateTicks => long.Parse(entity == null ? originalModifiedDate : entity.LastModifiedDate);

        public FileCache(string hash, string path, string lastModifiedDate)
        {
            originalHashNoEntity = hash;
            originalFilePathNoEntity = path;
            originalModifiedDate = lastModifiedDate;
        }

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
