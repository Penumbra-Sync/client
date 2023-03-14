using MareSynchronos.API.Dto.User;
using MareSynchronos.Services.ServerConfiguration;

namespace MareSynchronos.UI.Handlers;

public class TagHandler
{
    public const string CustomOfflineTag = "Mare_Offline";
    public const string CustomOnlineTag = "Mare_Online";
    public const string CustomUnpairedTag = "Mare_Unpaired";
    public const string CustomVisibleTag = "Mare_Visible";
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public TagHandler(ServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    public void AddTag(string tag)
    {
        _serverConfigurationManager.AddTag(tag);
    }

    public void AddTagToPairedUid(UserPairDto pair, string tagName)
    {
        _serverConfigurationManager.AddTagForUid(pair.User.UID, tagName);
    }

    public List<string> GetAllTagsSorted()
    {
        return _serverConfigurationManager.GetServerAvailablePairTags()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public HashSet<string> GetOtherUidsForTag(string tag)
    {
        return _serverConfigurationManager.GetUidsForTag(tag);
    }

    public bool HasAnyTag(UserPairDto pair)
    {
        return _serverConfigurationManager.HasTags(pair.User.UID);
    }

    public bool HasTag(UserPairDto pair, string tagName)
    {
        return _serverConfigurationManager.ContainsTag(pair.User.UID, tagName);
    }

    /// <summary>
    /// Is this tag opened in the paired clients UI?
    /// </summary>
    /// <param name="tag">the tag</param>
    /// <returns>open true/false</returns>
    public bool IsTagOpen(string tag)
    {
        return _serverConfigurationManager.ContainsOpenPairTag(tag);
    }

    public void RemoveTag(string tag)
    {
        // First remove the tag from teh available pair tags
        _serverConfigurationManager.RemoveTag(tag);
    }

    public void RemoveTagFromPairedUid(UserPairDto pair, string tagName)
    {
        _serverConfigurationManager.RemoveTagForUid(pair.User.UID, tagName);
    }

    public void SetTagOpen(string tag, bool open)
    {
        if (open)
        {
            _serverConfigurationManager.AddOpenPairTag(tag);
        }
        else
        {
            _serverConfigurationManager.RemoveOpenPairTag(tag);
        }
    }
}