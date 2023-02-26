using System.Text.Json;

namespace MareSynchronos.Utils;

public static class VariousExtensions
{
    public static T DeepClone<T>(this T obj)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj))!;
    }
}
