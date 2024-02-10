using Dalamud.Game.ClientState.Objects.SubKinds;

using System.Security.Cryptography;
using System.Text;

namespace MareSynchronos.Utils;

public static class Crypto
{
#pragma warning disable SYSLIB0021 // Type or member is obsolete

    private static readonly Dictionary<string, string> _hashListSHA1 = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _hashListSHA256 = new(StringComparer.Ordinal);
    private static readonly SHA256CryptoServiceProvider _sha256CryptoProvider = new();
    private static readonly SHA1CryptoServiceProvider _sha1CryptoProvider = new();

    public static string GetFileHash(this string filePath)
    {
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash(this string stringToHash)
    {
        return GetOrComputeHashSHA1(stringToHash);
    }

    public static string GetHash256(this string stringToHash)
    {
        return GetOrComputeHashSHA256(stringToHash);
    }

    public static string GetHash256(this PlayerCharacter character)
    {
        var charName = character.Name + character.HomeWorld.Id.ToString();
        return GetOrComputeHashSHA256(charName);
    }

    private static string GetOrComputeHashSHA1(string stringToCompute)
    {
        if (_hashListSHA1.TryGetValue(stringToCompute, out var hash))
            return hash;

        return _hashListSHA1[stringToCompute] = BitConverter.ToString(_sha1CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
    }

    private static string GetOrComputeHashSHA256(string stringToCompute)
    {
        if (_hashListSHA256.TryGetValue(stringToCompute, out var hash))
            return hash;

        return _hashListSHA256[stringToCompute] = BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
    }

#pragma warning restore SYSLIB0021 // Type or member is obsolete
}