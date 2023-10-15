using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI;

public class DrawEntityFactory
{
    private readonly ApiController _apiController;
    private readonly MareMediator _mediator;
    private readonly SelectPairForTagUi _selectPairForTagUi;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly IdDisplayHandler _uidDisplayHandler;

    public DrawEntityFactory(ApiController apiController, IdDisplayHandler uidDisplayHandler, SelectTagForPairUi selectTagForPairUi, MareMediator mediator,
        TagHandler tagHandler, SelectPairForTagUi selectPairForTagUi)
    {
        _apiController = apiController;
        _uidDisplayHandler = uidDisplayHandler;
        _selectTagForPairUi = selectTagForPairUi;
        _mediator = mediator;
        _tagHandler = tagHandler;
        _selectPairForTagUi = selectPairForTagUi;
    }

    public DrawFolderGroup CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto, List<Pair> pairs)
    {
        return new DrawFolderGroup(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController,
            pairs.Select(p => CreateGroupPair(groupFullInfoDto.Group.GID, p, groupFullInfoDto)),
            _tagHandler, _uidDisplayHandler, _mediator);
    }

    public DrawFolderTag CreateDrawTagFolder(string tag, Dictionary<Pair, List<GroupFullInfoDto>> pairs)
    {
        return new(tag, pairs
        .Select(u =>
        {
            if ((string.Equals(tag, TagHandler.CustomUnpairedTag, StringComparison.Ordinal) && u.Key.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.OneSided))
                return CreateDrawUserPair(tag, u.Key, u.Value);
            else
                return (DrawPairBase)DrawPairMultiSync(tag, u.Key, u.Value);
        }),
        _tagHandler, _apiController, _selectPairForTagUi);
    }

    public DrawPairIndividual CreateDrawUserPair(string id, Pair user, List<GroupFullInfoDto> groups)
    {
        return new DrawPairIndividual(id + user.UserData.UID, groups, user, _apiController, _uidDisplayHandler, _selectTagForPairUi, _mediator);
    }

    public DrawPairSingleGroup CreateGroupPair(string id, Pair user, GroupFullInfoDto groupFullInfo)
    {
        return new DrawPairSingleGroup(id + user.UserData.UID, groupFullInfo, user, _uidDisplayHandler, _apiController, _mediator, _selectTagForPairUi);
    }

    public DrawPairMultiSync DrawPairMultiSync(string id, Pair user, List<GroupFullInfoDto> groups)
    {
        return new DrawPairMultiSync(id + user.UserData.UID, groups, user, _apiController, _uidDisplayHandler, _mediator, _selectTagForPairUi);
    }
}