using System.Text;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data;

namespace MareSynchronos.PlayerData.Data;

public class CharacterData
{
    public Dictionary<ObjectKind, HashSet<FileReplacement>> FileReplacements { get; set; } = new();

    public Dictionary<ObjectKind, string> GlamourerString { get; set; } = new();

    public bool IsReady => FileReplacements.SelectMany(k => k.Value).All(f => f.Computed);

    public string ManipulationString { get; set; } = string.Empty;

    public float HeelsOffset { get; set; } = 0f;

    public string CustomizePlusScale { get; set; } = string.Empty;

    public string PalettePlusPalette { get; set; } = string.Empty;

    public API.Data.CharacterData ToAPI()
    {
        var fileReplacements = FileReplacements.ToDictionary(k => k.Key, k => k.Value.Where(f => f.HasFileReplacement && !f.IsFileSwap).GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase).Select(g =>
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
            HeelsOffset = HeelsOffset,
            CustomizePlusData = CustomizePlusScale,
            PalettePlusData = PalettePlusPalette,
        };
    }

    public override string ToString()
    {
        StringBuilder stringBuilder = new();
        foreach (var fileReplacement in FileReplacements.SelectMany(k => k.Value).OrderBy(a => a.GamePaths.First(), StringComparer.Ordinal))
        {
            stringBuilder.AppendLine(fileReplacement.ToString());
        }
        return stringBuilder.ToString();
    }
}
