using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MareSynchronos.Utils
{
    public class Crypto
    {
        public static string GetFileHash(string filePath)
        {
            using SHA1CryptoServiceProvider cryptoProvider = new();
            return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "");
        }

        public static string GetHash(string stringToHash)
        {
            using SHA1CryptoServiceProvider cryptoProvider = new();
            return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToHash))).Replace("-", "");
        }
    }
}
