using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.FileCache;
using System.Text;
using System.Text.Json;

namespace MareSynchronos.PlayerData.Export;

public record MareCharaFileData
{
    public string Description { get; set; } = string.Empty;
    public string GlamourerData { get; set; } = string.Empty;
    public string CustomizePlusData { get; set; } = string.Empty;
    public string PalettePlusData { get; set; } = string.Empty;
    public string ManipulationData { get; set; } = string.Empty;
    public List<FileData> Files { get; set; } = new();
    public List<FileSwap> FileSwaps { get; set; } = new();

    public MareCharaFileData() { }
    public MareCharaFileData(FileCacheManager manager, string description, CharacterData dto)
    {
        Description = description;

        if (dto.GlamourerData.TryGetValue(ObjectKind.Player, out var glamourerData))
        {
            GlamourerData = glamourerData;
        }

        dto.CustomizePlusData.TryGetValue(ObjectKind.Player, out var customizePlusData);
        CustomizePlusData = customizePlusData ?? string.Empty;
        PalettePlusData = dto.PalettePlusData;
        ManipulationData = dto.ManipulationData;

        if (dto.FileReplacements.TryGetValue(ObjectKind.Player, out var fileReplacements))
        {
            foreach (var file in fileReplacements)
            {
                if (!string.IsNullOrEmpty(file.FileSwapPath))
                {
                    FileSwaps.Add(new FileSwap(file.GamePaths, file.FileSwapPath));
                }
                else
                {
                    var filePath = manager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath;
                    if (filePath != null)
                    {
                        Files.Add(new FileData(file.GamePaths, new FileInfo(filePath).Length));
                    }
                }
            }
        }
    }

    public byte[] ToByteArray()
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));
    }

    public static MareCharaFileData FromByteArray(byte[] data)
    {
        return JsonSerializer.Deserialize<MareCharaFileData>(Encoding.UTF8.GetString(data))!;
    }

    public record FileSwap(IEnumerable<string> GamePaths, string FileSwapPath);

    public record FileData(IEnumerable<string> GamePaths, long Length);
}