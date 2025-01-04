using MareSynchronos.API.Dto.CharaData;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record CharaDataMetaInfoExtendedDto : CharaDataMetaInfoDto
{
    private CharaDataMetaInfoExtendedDto(CharaDataMetaInfoDto baseMeta) : base(baseMeta)
    {
    }

    public List<PoseEntryExtended> PoseExtended { get; private set; } = [];
    public bool HasPoses => PoseExtended.Count != 0;
    public bool HasWorldData => PoseExtended.Exists(p => p.HasWorldData);

    public async static Task<CharaDataMetaInfoExtendedDto> Create(CharaDataMetaInfoDto baseMeta, DalamudUtilService dalamudUtilService)
    {
        CharaDataMetaInfoExtendedDto newDto = new(baseMeta);

        foreach (var pose in newDto.PoseData)
        {
            newDto.PoseExtended.Add(await PoseEntryExtended.Create(pose, newDto, dalamudUtilService).ConfigureAwait(false));
        }

        return newDto;
    }
}
