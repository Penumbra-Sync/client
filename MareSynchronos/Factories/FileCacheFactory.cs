using System.IO;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;

namespace MareSynchronos.Factories
{
    public class FileCacheFactory
    {
        public FileCache Create(string file)
        {
            FileInfo fileInfo = new(file);
            string sha1Hash = Crypto.GetFileHash(fileInfo.FullName);
            return new FileCache()
            {
                Filepath = fileInfo.FullName,
                Hash = sha1Hash,
                LastModifiedDate = fileInfo.LastWriteTimeUtc.Ticks.ToString(),
            };
        }

        public void UpdateFileCache(FileCache cache)
        {
            FileInfo fileInfo = new(cache.Filepath);
            cache.Hash = Crypto.GetFileHash(cache.Filepath);
            cache.LastModifiedDate = fileInfo.LastWriteTimeUtc.Ticks.ToString();
        }
    }
}
