using MareSynchronos.API.Dto.CharaData;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record HandledCharaDataEntry(string Name, bool IsSelf, Guid? CustomizePlus, CharaDataMetaInfoDto MetaInfo);
