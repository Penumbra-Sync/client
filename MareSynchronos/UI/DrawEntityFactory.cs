using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MareSynchronos.UI;

public class DrawEntityFactory
{
    private readonly ILogger<DrawEntityFactory> _logger;
    private readonly ApiController _apiController;
    private readonly MareMediator _mediator;
    private readonly SelectPairForTagUi _selectPairForTagUi;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;

    public DrawEntityFactory(ILogger<DrawEntityFactory> logger, ApiController apiController, IdDisplayHandler uidDisplayHandler,
        SelectTagForPairUi selectTagForPairUi, MareMediator mediator,
        TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi,
        ServerConfigurationManager serverConfigurationManager)
    {
        _logger = logger;
        _apiController = apiController;
        _uidDisplayHandler = uidDisplayHandler;
        _selectTagForPairUi = selectTagForPairUi;
        _mediator = mediator;
        _tagHandler = tagHandler;
        _selectPairForTagUi = selectPairForTagUi;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto, IImmutableDictionary<Pair, List<GroupFullInfoDto>> pairs,
        IImmutableList<Pair> allPairs)
    {
        return new DrawFolderGroup(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController,
            pairs.Select(p => CreateDrawPair(groupFullInfoDto.Group.GID + p.Key.UserData.UID, p.Key, p.Value)).ToImmutableList(),
            allPairs, _tagHandler, _uidDisplayHandler, _mediator);
    }

    public DrawFolderTag CreateDrawTagFolder(string tag, IImmutableDictionary<Pair, List<GroupFullInfoDto>> pairs,
        IImmutableList<Pair> allPairs)
    {
        return new(tag, pairs.Select(u => CreateDrawPair(tag, u.Key, u.Value)).ToImmutableList(),
            allPairs, _tagHandler, _apiController, _selectPairForTagUi);
    }

    public DrawUserPair CreateDrawPair(string id, Pair user, List<GroupFullInfoDto> groups)
    {
        return new DrawUserPair(id + user.UserData.UID, user, groups, _apiController, _uidDisplayHandler,
            _mediator, _selectTagForPairUi, _serverConfigurationManager);
    }
}