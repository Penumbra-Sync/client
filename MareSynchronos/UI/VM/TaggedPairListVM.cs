using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Utils;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.UI.Components;
using Dalamud.Interface;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.UI.VM;

public enum CustomTag
{
    UserTag,
    AllUsers,
    Offline,
    Unpaired,
    Visible
}

public sealed class TaggedPairListVM : ImguiVM, IMediatorSubscriber, IDisposable
{
    private readonly MareConfigService _mareConfigService;
    private readonly PairManager _pairManager;
    private bool _reverse = false;
    private string _uiFilter = string.Empty;

    public TaggedPairListVM(string tag, CustomTag customTag, PairManager pairManager, Func<Pair, DrawUserPairVM> drawUserPairVMFactory,
        Func<DrawUserPairVM, DrawUserPair> drawUserPairFactory, TagHandler tagHandler, MareMediator mediator,
        Func<TaggedPairListVM, TagView> tagFactory, MareConfigService mareConfigService)
    {
        Tag = tag;
        CustomTag = customTag;
        _pairManager = pairManager;
        Mediator = mediator;
        _mareConfigService = mareConfigService;
        AllTagPairs = new(() => tagHandler.GetOtherUidsForTag(tag));
        Mediator.Subscribe<TagUpdateMessage>(this, (msg) =>
        {
            if (string.Equals(msg.Tag, Tag, StringComparison.Ordinal))
            {
                RecreateLazy();
            }
        });
        Mediator.Subscribe<PairManagerUpdateMessage>(this, (msg) =>
        {
            if (msg.User == null || msg.User != null && AllTagPairs.Value.Contains(msg.User.UID))
            {
                RecreateLazy();
            }
        });
        Mediator.Subscribe<SettingsChangedMessage<bool>>(this, (msg) =>
        {
            if (msg.ConfigName is not nameof(MareConfig)) return;

            if (msg.SettingName is nameof(MareConfig.ShowOfflineUsersSeparately) or nameof(MareConfig.ShowVisibleUsersSeparately))
            {
                RecreateLazy();
            }
        });

        UsersInTag = new(() =>
        {
            var result = _pairManager.DirectPairs.Where(p =>
            {
                bool isFiltered = string.IsNullOrEmpty(_uiFilter) ||
                        (p.UserData.AliasOrUID.Contains(_uiFilter, StringComparison.OrdinalIgnoreCase) ||
                        (p.GetNote()?.Contains(_uiFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (p.PlayerName?.Contains(_uiFilter, StringComparison.OrdinalIgnoreCase) ?? false));
                if (!isFiltered) return false;

                switch (CustomTag)
                {
                    case CustomTag.UserTag:
                        return AllTagPairs.Value.Contains(p.UserPair!.User.UID);

                    case CustomTag.AllUsers:
                    case CustomTag.Offline:
                    case CustomTag.Visible:
                        return true;

                    default:
                        return false;
                }
            })
            .OrderBy(p => p.PlayerName ?? p.GetNote() ?? p.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase)
            .Select(p => drawUserPairVMFactory(p)).ToList();
            if (_reverse) result.Reverse();
            return result;
        });
        OnlineUsers = new(() => UsersInTag.Value.Where(u => u.IsOnline || u.IsPausedFromSource).Select(c => drawUserPairFactory(c)).ToList());
        VisibleUsers = new(() => UsersInTag.Value.Where(u => u.IsVisible).Select(c => drawUserPairFactory(c)).ToList());
        OfflineUsers = new(() => UsersInTag.Value.Where(u => !u.IsOnline && !u.IsPausedFromSource).Select(c => drawUserPairFactory(c)).ToList());
        DrawnUsers = new(() =>
        {
            var userList = UsersInTag.Value.ToList();
            switch (CustomTag)
            {
                case CustomTag.UserTag:
                    if (_mareConfigService.Current.ShowOfflineUsersSeparately)
                        userList = userList.Where(u => u.IsOnline || u.IsPausedFromSource).ToList();
                    break;

                case CustomTag.AllUsers:
                    if (_mareConfigService.Current.ShowOfflineUsersSeparately)
                        userList = userList.Where(u => u.IsOnline || u.IsPausedFromSource).ToList();
                    break;

                case CustomTag.Offline:
                    if (_mareConfigService.Current.ShowOfflineUsersSeparately)
                        userList = userList.Where(u => !u.IsOnline || u.IsPausedFromTarget).ToList();
                    else userList = new();
                    break;

                case CustomTag.Visible:
                    if (_mareConfigService.Current.ShowVisibleUsersSeparately)
                        userList = userList.Where(u => u.IsVisible).ToList();
                    else userList = new();
                    break;
            }

            return userList.Select(c => drawUserPairFactory(c)).ToList();
        });

        RecreateLazy();
        SetupCommands();

        TagView = tagFactory(this);
    }

    public CustomTag CustomTag { get; }
    public ResettableLazy<List<DrawUserPair>> DrawnUsers { get; }
    public bool IsVisible => CustomTag != CustomTag.UserTag && DrawnUsers.Value.Any() || CustomTag == CustomTag.UserTag;
    public MareMediator Mediator { get; }
    public ResettableLazy<List<DrawUserPair>> OfflineUsers { get; }
    public ResettableLazy<List<DrawUserPair>> OnlineUsers { get; }
    public ButtonCommand PauseAllCommand { get; private set; } = new();
    public string Tag { get; }
    public TagView TagView { get; }
    public ResettableLazy<List<DrawUserPairVM>> UsersInTag { get; }

    public ResettableLazy<List<DrawUserPair>> VisibleUsers { get; }
    private ResettableLazy<HashSet<string>> AllTagPairs { get; }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    public void SetFilter(string filter)
    {
        _uiFilter = filter;
        RecreateLazy();
    }

    public void SetReverse(bool reverse)
    {
        _reverse = reverse;
        RecreateLazy();
    }

    private void RecreateLazy()
    {
        UsersInTag.Reset();
        OfflineUsers.Reset();
        OnlineUsers.Reset();
        AllTagPairs.Reset();
        VisibleUsers.Reset();
        DrawnUsers.Reset();
    }

    private void SetupCommands()
    {
        PauseAllCommand = new ButtonCommand()
            .WithRequireCtrl()
            .WithState(0, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Play)
                .WithAction(() =>
                {
                    foreach (DrawUserPairVM user in UsersInTag.Value.Where(p => p.IsPausedFromSource).ToList())
                    {
                        user.SetPaused(false);
                    }
                })
                .WithTooltip(() => "Resume " + UsersInTag.Value.Count(p => p.IsPausedFromSource) + " users")
                )
            .WithState(1, new ButtonCommand.State()
                .WithIcon(FontAwesomeIcon.Pause)
                .WithAction(() =>
                {
                    foreach (DrawUserPairVM user in UsersInTag.Value.Where(p => !p.IsPausedFromSource).ToList())
                    {
                        user.SetPaused(true);
                    }
                })
                .WithTooltip(() => "Pause " + UsersInTag.Value.Count(p => !p.IsPausedFromSource) + " users"))
            .WithStateSelector(() =>
            {
                if (UsersInTag.Value.All(p => p.IsPaused)) return 0;
                return 1;
            });

        var menu = new FlyoutMenuCommand()
            .WithCommand("Manage", new ButtonCommand());
    }
}