namespace MareSynchronos.Services.CharaData.Models;

public sealed record HandledCharaDataEntry(string Name, bool IsSelf, Guid? CustomizePlus, CharaDataMetaInfoExtendedDto MetaInfo)
{
    public CharaDataMetaInfoExtendedDto MetaInfo { get; set; } = MetaInfo;
}
