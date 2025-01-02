using MareSynchronos.API.Dto.CharaData;
using System.Collections.ObjectModel;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record CharaDataFullExtendedDto : CharaDataFullDto
{
    public CharaDataFullExtendedDto(CharaDataFullDto baseDto) : base(baseDto)
    {
        MissingFiles = new ReadOnlyCollection<GamePathEntry>(baseDto.OriginalFiles.Except(baseDto.FileGamePaths).ToList());
        HasMissingFiles = MissingFiles.Any();
    }

    public bool HasMissingFiles { get; init; }
    public IReadOnlyCollection<GamePathEntry> MissingFiles { get; init; }
}
