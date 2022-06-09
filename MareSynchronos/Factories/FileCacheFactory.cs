using System;
using System.IO;
using MareSynchronos.FileCacheDB;
using System.Security.Cryptography;


namespace MareSynchronos.Factories
{
    public class FileCacheFactory
    {
        public FileCacheFactory()
        {

        }

        public FileCache Create(string file)
        {
            FileInfo fileInfo = new(file);
            string sha1Hash = GetHash(fileInfo.FullName);
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
            cache.Hash = GetHash(cache.Filepath);
            cache.LastModifiedDate = fileInfo.LastWriteTimeUtc.Ticks.ToString();
        }

        private string GetHash(string filePath)
        {
            using SHA1CryptoServiceProvider cryptoProvider = new();
            return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath)));
        }
    }
}
