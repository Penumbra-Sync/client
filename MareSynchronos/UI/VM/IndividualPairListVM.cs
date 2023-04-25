using System.Diagnostics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.API.Data.Extensions;

namespace MareSynchronos.UI.VM;

public sealed class IndividualPairListVM : ImguiVM, IMediatorSubscriber, IDisposable
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly TagHandler _tagHandler;
    private string _characterFilter = string.Empty;
    private Stopwatch _timeout = new();

    public IndividualPairListVM(PairManager pairManager, MareConfigService mareConfigService,
        ApiController apiController, ServerConfigurationManager serverConfigurationManager, MareMediator mediator, TagHandler tagHandler,
        Func<string, CustomTag, TaggedPairListVM> taggedPairListVMFactory)
    {
        _pairManager = pairManager;
        _mareConfigService = mareConfigService;
        _apiController = apiController;
        _serverConfigurationManager = serverConfigurationManager;
        Mediator = mediator;
        _tagHandler = tagHandler;

        Mediator.Subscribe<PairManagerUpdateMessage>(this, (_) => RecreateLazy(false));
        Mediator.Subscribe<TagCreationMessage>(this, (_) => RecreateLazy(true));
        Mediator.Subscribe<TagDeletionMessage>(this, (_) => RecreateLazy(true));

        FilteredUsers = new(() => _pairManager.DirectPairs.Where(p =>
        {
            if (string.IsNullOrEmpty(CharacterOrCommentFilter)) return true;
            return p.UserData.AliasOrUID.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                    (p.GetNote()?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.PlayerName?.Contains(CharacterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList());

        Tags = new ResettableLazy<List<TaggedPairListVM>>(() =>
        {
            List<TaggedPairListVM> taggedPairListVMs = new List<TaggedPairListVM>();
            var tags = _tagHandler.GetAllTagsSorted();
            if (_mareConfigService.Current.ShowVisibleUsersSeparately) taggedPairListVMs.Add(taggedPairListVMFactory("Mare_Visible", CustomTag.Visible));
            foreach (var tag in tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                taggedPairListVMs.Add(taggedPairListVMFactory(tag, CustomTag.UserTag));
            }
            taggedPairListVMs.Add(taggedPairListVMFactory("Mare_Online", CustomTag.AllUsers));
            if (_mareConfigService.Current.ShowOfflineUsersSeparately) taggedPairListVMs.Add(taggedPairListVMFactory("Mare_Offline", CustomTag.Offline));
            taggedPairListVMs.Add(taggedPairListVMFactory("Mare_Unpaired", CustomTag.Unpaired));
            foreach (var vm in taggedPairListVMs)
            {
                vm.SetReverse(_mareConfigService.Current.ReverseUserSort);
            }
            return taggedPairListVMs;
        });

        RecreateLazy(true);
        SetupCommands();
    }

    public ButtonCommand AddPairCommand { get; private set; } = new();
    public string CharacterOrCommentFilter
    {
        get => _characterFilter;
        private set
        {
            _characterFilter = value;
            foreach (var tag in Tags.Value)
            {
                tag.SetFilter(_characterFilter);
            }
        }
    }
    public ResettableLazy<List<Pair>> FilteredUsers { get; }
    public Pair? LastAddedUser { get; private set; }
    public string LastAddedUserComment { get; private set; } = string.Empty;
    public ConditionalModal LastAddedUserModal { get; private set; } = new();
    public MareMediator Mediator { get; }
    public string PairToAdd { get; private set; } = string.Empty;
    public ButtonCommand PauseAllCommand { get; private set; } = new();
    public ButtonCommand ReverseSortCommand { get; private set; } = new();
    public ButtonCommand SetNoteForLastAddedUserCommand { get; private set; } = new();
    public ResettableLazy<List<TaggedPairListVM>> Tags { get; }
    private bool ReverseUserSort
    {
        get => _mareConfigService.Current.ReverseUserSort;
        set
        {
            _mareConfigService.Current.ReverseUserSort = value;
            _mareConfigService.Save();
            foreach (var tag in Tags.Value)
            {
                tag.SetReverse(value);
            }
        }
    }

    public void Dispose()
    {
        foreach (var value in Tags.Value)
        {
            value.Dispose();
        }
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

    private void RecreateLazy(bool resetTags)
    {
        FilteredUsers.Reset();
        if (resetTags)
        {
            foreach (var value in Tags.Value)
            {
                value.Dispose();
            }
            Tags.Reset();
        }
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
                    foreach (var user in FilteredUsers.Value.ToList())
                    {
                        var perm = user.UserPair!.OwnPermissions;
                        perm.SetPaused(true);
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
                    foreach (var user in FilteredUsers.Value.ToList())
                    {
                        var perm = user.UserPair!.OwnPermissions;
                        perm.SetPaused(false);
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

        LastAddedUserModal = new ConditionalModal()
            .WithTitle("Set User Note")
            .WithCondition(CheckLastAddedUser);
    }
}