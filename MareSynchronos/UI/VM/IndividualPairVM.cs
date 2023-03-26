using System.Diagnostics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.UI.Components;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services.Mediator;

namespace MareSynchronos.UI.VM;

public sealed class IndividualPairVM : ImguiVM, IMediatorSubscriber, IDisposable
{
    private readonly ApiController _apiController;
    private readonly Func<string, Pair, DrawUserPair> _drawUserPairFactory;
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private string _characterFilter = string.Empty;
    private Stopwatch _timeout = new();

    public IndividualPairVM(PairManager pairManager, MareConfigService mareConfigService, Func<string, Pair, DrawUserPair> drawUserPairFactory,
        ApiController apiController, ServerConfigurationManager serverConfigurationManager, MareMediator mediator)
    {
        _pairManager = pairManager;
        _mareConfigService = mareConfigService;
        _drawUserPairFactory = drawUserPairFactory;
        _apiController = apiController;
        _serverConfigurationManager = serverConfigurationManager;
        Mediator = mediator;

        Mediator.Subscribe<PairManagerUpdateMessage>(this, (_) => RecreateLazy());

        FilteredUsers = new(() => _pairManager.DirectPairs.Where(p =>
        {
            if (string.IsNullOrEmpty(CharacterOrCommentFilter)) return true;
            return p.UserData.AliasOrUID.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetNote()?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.PlayerName?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList());

        OnlineUsers = new ResettableLazy<List<DrawUserPair>>(() => GetFilteredUsers().Where(u => u.IsOnline || u.UserPair!.OwnPermissions.IsPaused())
            .Select(c => _drawUserPairFactory("Online" + c.UserData.UID, c)).ToList());
        VisibleUsers = new ResettableLazy<List<DrawUserPair>>(() => GetFilteredUsers().Where(u => u.IsVisible)
            .Select(c => _drawUserPairFactory("Visible" + c.UserData.UID, c)).ToList());
        OfflineUsers = new ResettableLazy<List<DrawUserPair>>(() => GetFilteredUsers().Where(u => !u.IsOnline && !u.UserPair!.OwnPermissions.IsPaused())
            .Select(c => _drawUserPairFactory("Offline" + c.UserData.UID, c)).ToList());

        RecreateLazy();
        SetupCommands();
    }

    public ButtonCommand AddPairCommand { get; private set; } = new();

    public string CharacterOrCommentFilter
    {
        get => _characterFilter;
        private set
        {
            _characterFilter = value;
            RecreateLazy();
        }
    }

    public ResettableLazy<List<Pair>> FilteredUsers { get; }

    public Pair? LastAddedUser { get; private set; }

    public string LastAddedUserComment { get; private set; } = string.Empty;

    public ConditionalModalVM LastAddedUserModal { get; private set; } = new();

    public MareMediator Mediator { get; }
    public ResettableLazy<List<DrawUserPair>> OfflineUsers { get; }

    public ResettableLazy<List<DrawUserPair>> OnlineUsers { get; }

    public string PairToAdd { get; private set; } = string.Empty;

    public ButtonCommand PauseAllCommand { get; private set; } = new();

    public ButtonCommand ReverseSortCommand { get; private set; } = new();

    public ButtonCommand SetNoteForLastAddedUserCommand { get; private set; } = new();

    public ResettableLazy<List<DrawUserPair>> VisibleUsers { get; }
    private bool ReverseUserSort
    {
        get => _mareConfigService.Current.ReverseUserSort;
        set
        {
            _mareConfigService.Current.ReverseUserSort = value;
            _mareConfigService.Save();
            RecreateLazy();
        }
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    private void AddPair()
    {
        _ = _apiController.UserAddPair(new(new(PairToAdd)));
        PairToAdd = string.Empty;
    }

    private bool CheckLastAddedUser()
    {
        if (!_mareConfigService.Current.OpenPopupOnAdd) return false;
        if (LastAddedUser != null) return true;
        if (_pairManager.LastAddedUser == null) return false;

        LastAddedUser = _pairManager.LastAddedUser;
        _pairManager.LastAddedUser = null;
        LastAddedUserComment = string.Empty;

        return true;
    }

    private List<Pair> GetFilteredUsers()
    {
        var users = FilteredUsers.Value
            .OrderBy(
                u => _mareConfigService.Current.ShowCharacterNameInsteadOfNotesForVisible && !string.IsNullOrEmpty(u.PlayerName)
                    ? u.PlayerName
                    : u.GetNote() ?? u.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase).ToList();

        if (ReverseUserSort)
        {
            users.Reverse();
        }

        return users;
    }

    private void RecreateLazy()
    {
        FilteredUsers.Reset();
        OnlineUsers.Reset();
        OfflineUsers.Reset();
        VisibleUsers.Reset();
    }

    private void SetNoteForLastAddedUser()
    {
        if (LastAddedUser == null) return;
        _serverConfigurationManager.SetNoteForUid(LastAddedUser.UserData.UID, LastAddedUserComment);
        LastAddedUser = null;
        LastAddedUserComment = string.Empty;
    }

    private void SetupCommands()
    {
        PauseAllCommand = new ButtonCommand()
            .WithRequireCtrl()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Play)
                .WithAction(() =>
                {
                    foreach (Pair user in FilteredUsers.Value.ToList())
                    {
                        var perm = user.UserPair!.OwnPermissions;
                        perm.SetPaused(false);
                        _ = _apiController.UserSetPairPermissions(new(user.UserData, perm));
                    }
                    _timeout = Stopwatch.StartNew();
                })
                .WithTooltip(() => "Resume " + FilteredUsers.Value.Count + " users")
                )
            .WithState(1, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Pause)
                .WithAction(() =>
                {
                    foreach (Pair user in FilteredUsers.Value.ToList())
                    {
                        var perm = user.UserPair!.OwnPermissions;
                        perm.SetPaused(true);
                        _ = _apiController.UserSetPairPermissions(new(user.UserData, perm));
                    }
                    _timeout = Stopwatch.StartNew();
                })
                .WithTooltip(() => "Pause " + FilteredUsers.Value.Count + " users"))
            .WithState(2, new ButtonCommand.State()
        .WithIcon(FontAwesomeIcon.Clock)
        .WithEnabled(false)
                .WithTooltip(() => $"Wait {TimeSpan.FromMilliseconds(15000 - _timeout.ElapsedMilliseconds).TotalSeconds.ToString("0")}s until you can pause/resume again"))
        .WithStateSelector(() =>
        {
            if (_timeout.IsRunning && _timeout.ElapsedMilliseconds < 15000)
            {
                return 2;
            }
            if (_timeout.IsRunning && _timeout.ElapsedMilliseconds > 15000)
            {
                _timeout.Stop();
            }
            if (FilteredUsers.Value.All(p => p.IsPaused)) return 0;
            return 1;
        });

        ReverseSortCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.ArrowDown)
                .WithAction(() => ReverseUserSort = !ReverseUserSort)
                .WithTooltip("Sort users descending by name"))
            .WithState(1, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.ArrowUp)
                .WithAction(() => ReverseUserSort = !ReverseUserSort)
                .WithTooltip("Sort users ascending by name"))
            .WithStateSelector(() => ReverseUserSort ? 1 : 0);

        AddPairCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Plus)
                .WithTooltip(() => "Pair with " + PairToAdd)
                .WithAction(AddPair))
            .WithState(1, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Plus)
                .WithEnabled(false)
                .WithTooltip("Enter a UID to pair with that user"))
            .WithState(2, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Ban)
                .WithForeground(ImGuiColors.DalamudRed)
                .WithEnabled(false)
                .WithTooltip(() => "You are already paired with " + PairToAdd))
            .WithStateSelector(() =>
            {
                if (string.IsNullOrEmpty(PairToAdd))
                    return 1;
                if (_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, PairToAdd, StringComparison.Ordinal)
                    || string.Equals(p.UserData.Alias, PairToAdd, StringComparison.Ordinal)))
                    return 2;
                return 0;
            });

        SetNoteForLastAddedUserCommand = new ButtonCommand()
            .WithState(0, new ButtonCommand.State().WithAction(SetNoteForLastAddedUser)
                .WithText("Save Note")
                .WithIcon(FontAwesomeIcon.Save));

        LastAddedUserModal = new ConditionalModalVM()
            .WithTitle("Set User Note")
            .WithCondition(CheckLastAddedUser);
    }
}