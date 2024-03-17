using MareSynchronos.API.Data;

namespace MareSynchronos.PlayerData.Data;

public class FileReplacementDataComparer : IEqualityComparer<FileReplacementData>
{
    private static readonly FileReplacementDataComparer _instance = new();

    private FileReplacementDataComparer()
    { }

    public static FileReplacementDataComparer Instance => _instance;

    public bool Equals(FileReplacementData? x, FileReplacementData? y)
    {
        if (x == null || y == null) return false;
        return x.Hash.Equals(y.Hash) && CompareHashSets(x.GamePaths.ToHashSet(StringComparer.Ordinal), y.GamePaths.ToHashSet(StringComparer.Ordinal)) && string.Equals(x.FileSwapPath, y.FileSwapPath, StringComparison.Ordinal);
    }

    public int GetHashCode(FileReplacementData obj)
    {
        return HashCode.Combine(obj.Hash.GetHashCode(StringComparison.OrdinalIgnoreCase), GetOrderIndependentHashCode(obj.GamePaths), StringComparer.Ordinal.GetHashCode(obj.FileSwapPath));
    }

    private static bool CompareHashSets(HashSet<string> list1, HashSet<string> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        for (int i = 0; i < list1.Count; i++)
        {
            if (!string.Equals(list1.ElementAt(i), list2.ElementAt(i), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int GetOrderIndependentHashCode<T>(IEnumerable<T> source) where T : notnull
    {
        int hash = 0;
        foreach (T element in source)
        {
            hash = unchecked(hash +
                EqualityComparer<T>.Default.GetHashCode(element));
        }
        return hash;
    }
}