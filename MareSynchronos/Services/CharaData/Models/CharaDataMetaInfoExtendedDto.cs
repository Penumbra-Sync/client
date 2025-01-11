using MareSynchronos.API.Dto.CharaData;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record CharaDataMetaInfoExtendedDto : CharaDataMetaInfoDto
{
    private CharaDataMetaInfoExtendedDto(CharaDataMetaInfoDto baseMeta) : base(baseMeta)
    {
        FullId = baseMeta.Uploader.UID + ":" + baseMeta.Id;
    }

    public List<PoseEntryExtended> PoseExtended { get; private set; } = [];
    public bool HasPoses => PoseExtended.Count != 0;
    public bool HasWorldData => PoseExtended.Exists(p => p.HasWorldData);
    public bool IsOwnData { get; private set; }
    public string FullId { get; private set; }

    public async static Task<CharaDataMetaInfoExtendedDto> Create(CharaDataMetaInfoDto baseMeta, DalamudUtilService dalamudUtilService, bool isOwnData = false)
    {
        CharaDataMetaInfoExtendedDto newDto = new(baseMeta);

        foreach (var pose in newDto.PoseData)
        {
            newDto.PoseExtended.Add(await PoseEntryExtended.Create(pose, newDto, dalamudUtilService).ConfigureAwait(false));
        }

        newDto.IsOwnData = isOwnData;

        return newDto;
    }
}
