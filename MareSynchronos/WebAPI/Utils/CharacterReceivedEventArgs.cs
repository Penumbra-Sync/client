namespace MareSynchronos.WebAPI.Utils;

public class CharacterReceivedEventArgs : EventArgs
{
    public CharacterReceivedEventArgs(string characterNameHash, CharacterCacheDto characterData)
    {
        CharacterData = characterData;
        CharacterNameHash = characterNameHash;
    }

    public CharacterCacheDto CharacterData { get; set; }
    public string CharacterNameHash { get; set; }
}