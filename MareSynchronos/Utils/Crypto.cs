using System.Security.Cryptography;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace MareSynchronos.Utils;

public static class Crypto
{
    public static string GetFileHash(string filePath)
    {
        return BitConverter.ToString(SHA1.HashData(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash(this string stringToHash)
    {
        return BitConverter.ToString(SHA1.HashData(Encoding.UTF8.GetBytes(stringToHash))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this string stringToHash)
    {
        return BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(stringToHash))).Replace("-", "", StringComparison.Ordinal);
    }

    public static string GetHash256(this PlayerCharacter character)
    {
        return BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(character.Name + character.HomeWorld.Id.ToString()))).Replace("-", "", StringComparison.Ordinal);
    }
}
