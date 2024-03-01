using MareSynchronos.API.Data;

using MareSynchronos.API.Data.Enum;

namespace MareSynchronos.PlayerData.Data;

public class CharacterData
{
    public Dictionary<ObjectKind, string> CustomizePlusScale { get; set; } = [];
    public Dictionary<ObjectKind, HashSet<FileReplacement>> FileReplacements { get; set; } = [];

    public Dictionary<ObjectKind, string> GlamourerString { get; set; } = [];

    public string HeelsData { get; set; } = string.Empty;
    public string HonorificData { get; set; } = string.Empty;
    public string ManipulationString { get; set; } = string.Empty;
    public string MoodlesData { get; set; } = string.Empty;

    public API.Data.CharacterData ToAPI()
    {
        Dictionary<ObjectKind, List<FileReplacementData>> fileReplacements =
            FileReplacements.ToDictionary(k => k.Key, k => k.Value.Where(f => f.HasFileReplacement && !f.IsFileSwap)
            .GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
        {
            return new FileReplacementData()
            {
                GamePaths = g.SelectMany(f => f.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Hash = g.First().Hash,
            };
        }).ToList());

        foreach (var item in FileReplacements)
        {
            var fileSwapsToAdd = item.Value.Where(f => f.IsFileSwap).Select(f => f.ToFileReplacementDto());
            fileReplacements[item.Key].AddRange(fileSwapsToAdd);
        }

        return new API.Data.CharacterData()
        {
            FileReplacements = fileReplacements,
            GlamourerData = GlamourerString.ToDictionary(d => d.Key, d => d.Value),
            ManipulationData = ManipulationString,
            HeelsData = HeelsData,
            CustomizePlusData = CustomizePlusScale.ToDictionary(d => d.Key, d => d.Value),
            HonorificData = HonorificData,
            MoodlesData = MoodlesData
        };
    }
}