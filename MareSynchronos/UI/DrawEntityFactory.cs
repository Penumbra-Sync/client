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
    private readonly IdDisplayHandler _uidDisplayHandler;
    private readonly SelectTagForPairUi _selectTagForPairUi;
    private readonly MareMediator _mediator;
    private readonly TagHandler _tagHandler;
    private readonly SelectPairForTagUi _selectPairForTagUi;

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

    public DrawTagFolder CreateDrawTagFolder(string tag, List<Pair> pairs)
    {
        return new(TagHandler.CustomVisibleTag, pairs
        .Select(u =>
        {
            if (u.IndividualPairStatus == API.Data.Enum.IndividualPairStatus.Bidirectional)
                return (DrawPairBase)CreateDrawUserPair(tag, u);
            else
                return (DrawPairBase)CreateUngroupedGroupPair(tag, u);
        }),
        _tagHandler, _apiController, _selectPairForTagUi);
    }

    public DrawGroupFolder CreateDrawGroupFolder(GroupFullInfoDto groupFullInfoDto, List<Pair> pairs)
    {
        return new DrawGroupFolder(groupFullInfoDto.Group.GID, groupFullInfoDto, _apiController,
            pairs.Select(p => CreateGroupPair(groupFullInfoDto.Group.GID, p, groupFullInfoDto)),
            _tagHandler, _uidDisplayHandler, _mediator);
    }

    public DrawUserPair CreateDrawUserPair(string id, Pair user)
    {
        return new DrawUserPair(id + user.UserData.UID, user, _apiController, _uidDisplayHandler, _selectTagForPairUi, _mediator);
    }

    public DrawUngroupedGroupPair CreateUngroupedGroupPair(string id, Pair user)
    {
        return new DrawUngroupedGroupPair(id + user.UserData.UID, user, _apiController, _uidDisplayHandler, _mediator);
    }

    public DrawGroupPair CreateGroupPair(string id, Pair user, GroupFullInfoDto groupFullInfo)
    {
        return new DrawGroupPair(id + user.UserData.UID, groupFullInfo, user, _uidDisplayHandler, _apiController, _mediator);
    }
}
