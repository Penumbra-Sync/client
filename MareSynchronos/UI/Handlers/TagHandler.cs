using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;

namespace MareSynchronos.UI.Handlers;

public class TagHandler
{
    public const string CustomOfflineTag = "Mare_Offline";
    public const string CustomOnlineTag = "Mare_Online";
    public const string CustomUnpairedTag = "Mare_Unpaired";
    public const string CustomVisibleTag = "Mare_Visible";
    private readonly MareMediator _mareMediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public TagHandler(ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _mareMediator = mareMediator;
    }

    public void AddTag(string tag)
    {
        _serverConfigurationManager.AddTag(tag);
        _mareMediator.Publish(new TagCreationMessage(tag));
    }

    public void AddTagToPairedUid(string uid, string tagName)
    {
        _serverConfigurationManager.AddTagForUid(uid, tagName);
        _mareMediator.Publish(new TagUpdateMessage(tagName));
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

    public bool HasAnyTag(string uid)
    {
        return _serverConfigurationManager.HasTags(uid);
    }

    public bool HasTag(string uid, string tagName)
    {
        return _serverConfigurationManager.ContainsTag(uid, tagName);
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
        _mareMediator.Publish(new TagDeletionMessage(tag));
    }

    public void RemoveTagFromPairedUid(string uid, string tagName)
    {
        _serverConfigurationManager.RemoveTagForUid(uid, tagName);
        _mareMediator.Publish(new TagUpdateMessage(tagName));
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