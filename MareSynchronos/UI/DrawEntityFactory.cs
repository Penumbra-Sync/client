using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class DrawEntityFactory
{
    private readonly ILogger<DrawEntityFactory> _logger;
    private readonly ApiController _apiController;
    private readonly MareMediator _mediator;
    private readonly SelectPairForTagUi _selectPairForTagUi;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;

    public DrawEntityFactory(ILogger<DrawEntityFactory> logger, ApiController apiController, IdDisplayHandler uidDisplayHandler, SelectTagForPairUi selectTagForPairUi, MareMediator mediator,
        TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi)
    {
        _logger = logger;
        _apiController = apiController;
        _uidDisplayHandler = uidDisplayHandler;
        _selectTagForPairUi = selectTagForPairUi;
        _mediator = mediator;
        _tagHandler = tagHandler;
        _selectPairForTagUi = selectPairForTagUi;
    }

    public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto, Dictionary<Pair, List<GroupFullInfoDto>> pairs)
    {
        _logger.LogTrace("Creating new DrawGroupFolder for {gid}", groupFullInfoDto.GID);
        return new DrawFolderGroup(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController,
            pairs.Select(p => CreateDrawPair(groupFullInfoDto.Group.GID + p.Key.UserData.UID, p.Key, p.Value)).ToList(),
            _tagHandler, _uidDisplayHandler, _mediator);
    }

    public DrawFolderTag CreateDrawTagFolder(string tag, Dictionary<Pair, List<GroupFullInfoDto>> pairs)
    {
        _logger.LogTrace("Creating new DrawTagFolder for {tag}", tag);

        return new(tag, pairs.Select(u => CreateDrawPair(tag, u.Key, u.Value)).ToList(),
            _tagHandler, _apiController, _selectPairForTagUi);
    }

    public DrawUserPair CreateDrawPair(string id, Pair user, List<GroupFullInfoDto> groups)
    {
        _logger.LogTrace("Creating new DrawPair for {id}", id + user.UserData.UID);

        return new DrawUserPair(id + user.UserData.UID, user, groups, _apiController, _uidDisplayHandler, _mediator, _selectTagForPairUi);
    }
}