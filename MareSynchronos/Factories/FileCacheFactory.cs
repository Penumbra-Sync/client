using System;
using System.IO;
using System.Threading;
using MareSynchronos.FileCacheDB;
using MareSynchronos.Utils;

namespace MareSynchronos.Factories
{
    public class FileCacheFactory
    {
        public FileCache Create(string file)
        {
            FileInfo fileInfo = new(file);
            if (IsFileLocked(fileInfo))
            {
                throw new FileLoadException();
            }
            var sha1Hash = Crypto.GetFileHash(fileInfo.FullName);
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

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using var fs = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                return true;
            }

            return false;
        }
    }
}
