using System.Security.Cryptography;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace MareSynchronos.Utils;

public static class Crypto
{
    public static string GetFileHash(this string filePath)
    {
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash(this string stringToHash)
    {
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToHash))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this string stringToHash)
    {
        using SHA256CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToHash))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this PlayerCharacter character)
    {
        using SHA256CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(character.Name + character.HomeWorld.Id.ToString()))).Replace("-", "", StringComparison.Ordinal);
    }
}
