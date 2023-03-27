using System.Security.Cryptography;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace MareSynchronos.Utils;

public static class Crypto
{
    private static readonly Dictionary<string, string> _hashListSHA1 = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _hashListSHA256 = new(StringComparer.Ordinal);

#pragma warning disable SYSLIB0021 // Type or member is obsolete

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

        using SHA1CryptoServiceProvider cryptoProvider = new();
        var computedHash = BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
        _hashListSHA1[stringToCompute] = computedHash;
        return computedHash;
    }

    private static string GetOrComputeHashSHA256(string stringToCompute)
    {
        if (_hashListSHA256.TryGetValue(stringToCompute, out var hash))
            return hash;

        using SHA256CryptoServiceProvider cryptoProvider = new();
        var computedHash = BitConverter.ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
        _hashListSHA256[stringToCompute] = computedHash;
        return computedHash;
    }

#pragma warning restore SYSLIB0021 // Type or member is obsolete
}