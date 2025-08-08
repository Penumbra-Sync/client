using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.CharaData;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi : WindowMediatorSubscriberBase
{
    private const int maxPoses = 10;
    private readonly CharaDataManager _charaDataManager;
    private readonly CharaDataNearbyManager _charaDataNearbyManager;
    private readonly CharaDataConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly PairManager _pairManager;
    private readonly CharaDataGposeTogetherManager _charaDataGposeTogetherManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private CancellationTokenSource _closalCts = new();
    private bool _disableUI = false;
    private CancellationTokenSource _disposalCts = new();
    private string _exportDescription = string.Empty;
    private string _filterCodeNote = string.Empty;
    private string _filterDescription = string.Empty;
    private Dictionary<string, List<CharaDataMetaInfoExtendedDto>>? _filteredDict;
    private Dictionary<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto? MetaInfo, bool DownloadedMetaInfo)> _filteredFavorites = [];
    private bool _filterPoseOnly = false;
    private bool _filterWorldOnly = false;
    private string _gposeTarget = string.Empty;
    private bool _hasValidGposeTarget;
    private string _importCode = string.Empty;
    private bool _isHandlingSelf = false;
    private DateTime _lastFavoriteUpdateTime = DateTime.UtcNow;
    private PoseEntryExtended? _nearbyHovered;
    private bool _openMcdOnlineOnNextRun = false;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string SelectedDtoId
    {
        get => _selectedDtoId;
        set
        {
            if (!string.Equals(_selectedDtoId, value, StringComparison.Ordinal))
            {
                _charaDataManager.UploadTask = null;
                _selectedDtoId = value;
            }

        }
    }
    private string _selectedSpecificUserIndividual = string.Empty;
    private string _selectedSpecificGroupIndividual = string.Empty;
    private string _sharedWithYouDescriptionFilter = string.Empty;
    private bool _sharedWithYouDownloadableFilter = false;
    private string _sharedWithYouOwnerFilter = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private string _specificGroupAdd = string.Empty;
    private bool _abbreviateCharaName = false;
    private string? _openComboHybridId = null;
    private (string Id, string? Alias, string AliasOrId, string? Note)[]? _openComboHybridEntries = null;
    private bool _comboHybridUsedLastFrame = false;

    public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, CharaDataNearbyManager charaDataNearbyManager, CharaDataConfigService configService,
                         UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager,
                         DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager, PairManager pairManager,
                         CharaDataGposeTogetherManager charaDataGposeTogetherManager)
        : base(logger, mediator, "Mare Synchronos Character Data Hub###MareSynchronosCharaDataUI", performanceCollectorService)
    {
        SetWindowSizeConstraints();

        _charaDataManager = charaDataManager;
        _charaDataNearbyManager = charaDataNearbyManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _fileDialogManager = fileDialogManager;
        _pairManager = pairManager;
        _charaDataGposeTogetherManager = charaDataGposeTogetherManager;
        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen |= _configService.Current.OpenMareHubOnGposeStart);
        Mediator.Subscribe<OpenCharaDataHubWithFilterMessage>(this, (msg) =>
        {
            IsOpen = true;
            _openDataApplicationShared = true;
            _sharedWithYouOwnerFilter = msg.UserData.AliasOrUID;
            UpdateFilteredItems();
        });
    }

    private bool _openDataApplicationShared = false;

    public string CharaName(string name)
    {
        if (_abbreviateCharaName)
        {
            var split = name.Split(" ");
            return split[0].First() + ". " + split[1].First() + ".";
        }

        return name;
    }

    public override void OnClose()
    {
        if (_disableUI)
        {
            IsOpen = true;
            return;
        }

        _closalCts.Cancel();
        SelectedDtoId = string.Empty;
        _filteredDict = null;
        _sharedWithYouOwnerFilter = string.Empty;
        _importCode = string.Empty;
        _charaDataNearbyManager.ComputeNearbyData = false;
        _openComboHybridId = null;
        _openComboHybridEntries = null;
    }

    public override void OnOpen()
    {
        _closalCts = _closalCts.CancelRecreate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closalCts.CancelDispose();
            _disposalCts.CancelDispose();
        }

        base.Dispose(disposing);
    }

    protected override void DrawInternal()
    {
        if (!_comboHybridUsedLastFrame)
        {
            _openComboHybridId = null;
            _openComboHybridEntries = null;
        }
        _comboHybridUsedLastFrame = false;

        _disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true);
        if (DateTime.UtcNow.Subtract(_lastFavoriteUpdateTime).TotalSeconds > 2)
        {
            _lastFavoriteUpdateTime = DateTime.UtcNow;
            UpdateFilteredFavorites();
        }

        (_hasValidGposeTarget, _gposeTarget) = _charaDataManager.CanApplyInGpose().GetAwaiter().GetResult();

        if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText("To utilize any features related to posing or spawning characters you require to have Brio installed.", ImGuiColors.DalamudRed);
            UiSharedService.DistanceSeparator();
        }

        using var disabled = ImRaii.Disabled(_disableUI);

        DisableDisabled(() =>
        {
            if (_charaDataManager.DataApplicationTask != null)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Applying Data to Actor");
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Cancel Application"))
                {
                    _charaDataManager.CancelDataApplication();
                }
            }
            if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
            {
                UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, ImGuiColors.DalamudYellow);
            }
            if (_charaDataManager.DataApplicationTask != null)
            {
                UiSharedService.ColorTextWrapped("WARNING: During the data application avoid interacting with this actor to prevent potential crashes.", ImGuiColors.DalamudRed);
                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
            }
        });

        using var tabs = ImRaii.TabBar("TabsTopLevel");
        bool smallUi = false;

        _isHandlingSelf = _charaDataManager.HandledCharaData.Any(c => c.IsSelf);
        if (_isHandlingSelf) _openMcdOnlineOnNextRun = false;

        using (var gposeTogetherTabItem = ImRaii.TabItem("GPose Together"))
        {
            if (gposeTogetherTabItem)
            {
                smallUi = true;

                DrawGposeTogether();
            }
        }

        using (var applicationTabItem = ImRaii.TabItem("Data Application", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            if (applicationTabItem)
            {
                smallUi = true;
                using var appTabs = ImRaii.TabBar("TabsApplicationLevel");

                using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
                {
                    using (var gposeTabItem = ImRaii.TabItem("GPose Actors"))
                    {
                        if (gposeTabItem)
                        {
                            using var id = ImRaii.PushId("gposeControls");
                            DrawGposeControls();
                        }
                    }
                }
                if (!_uiSharedService.IsInGpose)
                    UiSharedService.AttachToolTip("Only available in GPose");

                using (var nearbyPosesTabItem = ImRaii.TabItem("Poses Nearby"))
                {
                    if (nearbyPosesTabItem)
                    {
                        using var id = ImRaii.PushId("nearbyPoseControls");
                        _charaDataNearbyManager.ComputeNearbyData = true;

                        DrawNearbyPoses();
                    }
                    else
                    {
                        _charaDataNearbyManager.ComputeNearbyData = false;
                    }
                }

                using (var gposeTabItem = ImRaii.TabItem("Apply Data", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                {
                    if (gposeTabItem)
                    {
                        smallUi |= true;
                        using var id = ImRaii.PushId("applyData");
                        DrawDataApplication();
                    }
                }
            }
            else
            {
                _charaDataNearbyManager.ComputeNearbyData = false;
            }
        }

        using (ImRaii.Disabled(_isHandlingSelf))
        {
            ImGuiTabItemFlags flagsTopLevel = ImGuiTabItemFlags.None;
            if (_openMcdOnlineOnNextRun)
            {
                flagsTopLevel = ImGuiTabItemFlags.SetSelected;
                _openMcdOnlineOnNextRun = false;
            }

            using (var creationTabItem = ImRaii.TabItem("Data Creation", flagsTopLevel))
            {
                if (creationTabItem)
                {
                    using var creationTabs = ImRaii.TabBar("TabsCreationLevel");

                    ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                    if (_openMcdOnlineOnNextRun)
                    {
                        flags = ImGuiTabItemFlags.SetSelected;
                        _openMcdOnlineOnNextRun = false;
                    }
                    using (var mcdOnlineTabItem = ImRaii.TabItem("MCD Online", flags))
                    {
                        if (mcdOnlineTabItem)
                        {
                            using var id = ImRaii.PushId("mcdOnline");
                            DrawMcdOnline();
                        }
                    }

                    using (var mcdfTabItem = ImRaii.TabItem("MCDF Export"))
                    {
                        if (mcdfTabItem)
                        {
                            using var id = ImRaii.PushId("mcdfExport");
                            DrawMcdfExport();
                        }
                    }
                }
            }
        }
        if (_isHandlingSelf)
        {
            UiSharedService.AttachToolTip("Cannot use creation tools while having Character Data applied to self.");
        }

        using (var settingsTabItem = ImRaii.TabItem("Settings"))
        {
            if (settingsTabItem)
            {
                using var id = ImRaii.PushId("settings");
                DrawSettings();
            }
        }


        SetWindowSizeConstraints(smallUi);
    }

    private void DrawAddOrRemoveFavorite(CharaDataFullDto dto)
    {
        DrawFavorite(dto.Uploader.UID + ":" + dto.Id);
    }

    private void DrawAddOrRemoveFavorite(CharaDataMetaInfoExtendedDto? dto)
    {
        if (dto == null) return;
        DrawFavorite(dto.FullId);
    }

    private void DrawFavorite(string id)
    {
        bool isFavorite = _configService.Current.FavoriteCodes.TryGetValue(id, out var favorite);
        if (_configService.Current.FavoriteCodes.ContainsKey(id))
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.ParsedGold);
            UiSharedService.AttachToolTip($"Custom Description: {favorite?.CustomDescription ?? string.Empty}" + UiSharedService.TooltipSeparator
                + "Click to remove from Favorites");
        }
        else
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.DalamudGrey);
            UiSharedService.AttachToolTip("Click to add to Favorites");
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isFavorite) _configService.Current.FavoriteCodes.Remove(id);
            else _configService.Current.FavoriteCodes[id] = new();
            _configService.Save();
        }
    }

    private void DrawGposeControls()
    {
        _uiSharedService.BigText("GPose Actors");
        ImGuiHelpers.ScaledDummy(5);
        using var indent = ImRaii.PushIndent(10f);

        foreach (var actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
        {
            if (actor == null) continue;
            using var actorId = ImRaii.PushId(actor.Name.TextValue);
            UiSharedService.DrawGrouped(() =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
                {
                    unsafe
                    {
                        _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
                    }
                }
                ImGui.SameLine();
                UiSharedService.AttachToolTip($"Target the GPose Character {CharaName(actor.Name.TextValue)}");
                ImGui.AlignTextToFramePadding();
                var pos = ImGui.GetCursorPosX();
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (_dalamudUtilService.GetGposeTargetGameObjectAsync().GetAwaiter().GetResult()?.Address ?? nint.Zero)))
                {
                    ImGui.TextUnformatted(CharaName(actor.Name.TextValue));
                }
                ImGui.SameLine(250);
                var handled = _charaDataManager.HandledCharaData.FirstOrDefault(c => string.Equals(c.Name, actor.Name.TextValue, StringComparison.Ordinal));
                using (ImRaii.Disabled(handled == null))
                {
                    _uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
                    var id = string.IsNullOrEmpty(handled?.MetaInfo.Uploader.UID) ? handled?.MetaInfo.Id : handled.MetaInfo.FullId;
                    UiSharedService.AttachToolTip($"Applied Data: {id ?? "No data applied"}");

                    ImGui.SameLine();
                    // maybe do this better, check with brio for handled charas or sth
                    using (ImRaii.Disabled(!actor.Name.TextValue.StartsWith("Brio ", StringComparison.Ordinal)))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                        {
                            _charaDataManager.RemoveChara(actor.Name.TextValue);
                        }
                        UiSharedService.AttachToolTip($"Remove character {CharaName(actor.Name.TextValue)}");
                    }
                    ImGui.SameLine();
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                    {
                        _charaDataManager.RevertChara(handled);
                    }
                    UiSharedService.AttachToolTip($"Revert applied data from {CharaName(actor.Name.TextValue)}");
                    ImGui.SetCursorPosX(pos);
                    DrawPoseData(handled?.MetaInfo, actor.Name.TextValue, true);
                }
            });

            ImGuiHelpers.ScaledDummy(2);
        }
    }

    private void DrawDataApplication()
    {
        _uiSharedService.BigText("Apply Character Appearance");

        ImGuiHelpers.ScaledDummy(5);

        if (_uiSharedService.IsInGpose)
        {
            ImGui.TextUnformatted("GPose Target");
            ImGui.SameLine(200);
            UiSharedService.ColorText(CharaName(_gposeTarget), UiSharedService.GetBoolColor(_hasValidGposeTarget));
        }

        if (!_hasValidGposeTarget)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText("Applying data is only available in GPose with a valid selected GPose target.", ImGuiColors.DalamudYellow, 350);
        }

        ImGuiHelpers.ScaledDummy(10);

        using var tabs = ImRaii.TabBar("Tabs");

        using (var byFavoriteTabItem = ImRaii.TabItem("Favorites"))
        {
            if (byFavoriteTabItem)
            {
                using var id = ImRaii.PushId("byFavorite");

                ImGuiHelpers.ScaledDummy(5);

                var max = ImGui.GetWindowContentRegionMax();
                UiSharedService.DrawTree("Filters", () =>
                {
                    var maxIndent = ImGui.GetWindowContentRegionMax();
                    ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                    ImGui.InputTextWithHint("##ownFilter", "Code/Owner Filter", ref _filterCodeNote, 100);
                    ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                    ImGui.InputTextWithHint("##descFilter", "Custom Description Filter", ref _filterDescription, 100);
                    ImGui.Checkbox("Only show entries with pose data", ref _filterPoseOnly);
                    ImGui.Checkbox("Only show entries with world data", ref _filterWorldOnly);
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Reset Filter"))
                    {
                        _filterCodeNote = string.Empty;
                        _filterDescription = string.Empty;
                        _filterPoseOnly = false;
                        _filterWorldOnly = false;
                    }
                });

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var scrollableChild = ImRaii.Child("favorite");
                ImGuiHelpers.ScaledDummy(5);
                using var totalIndent = ImRaii.PushIndent(5f);
                var cursorPos = ImGui.GetCursorPos();
                max = ImGui.GetWindowContentRegionMax();
                foreach (var favorite in _filteredFavorites.OrderByDescending(k => k.Value.Favorite.LastDownloaded))
                {
                    UiSharedService.DrawGrouped(() =>
                    {
                        using var tableid = ImRaii.PushId(favorite.Key);
                        ImGui.AlignTextToFramePadding();
                        DrawFavorite(favorite.Key);
                        using var innerIndent = ImRaii.PushIndent(25f);
                        ImGui.SameLine();
                        var xPos = ImGui.GetCursorPosX();
                        var maxPos = (max.X - cursorPos.X);

                        bool metaInfoDownloaded = favorite.Value.DownloadedMetaInfo;
                        var metaInfo = favorite.Value.MetaInfo;

                        ImGui.AlignTextToFramePadding();
                        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !metaInfoDownloaded))
                        using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), metaInfoDownloaded))
                            ImGui.TextUnformatted(favorite.Key);

                        var iconSize = _uiSharedService.GetIconSize(FontAwesomeIcon.Check);
                        var refreshButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
                        var applyButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight);
                        var addButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
                        var offsetFromRight = maxPos - (iconSize.X + refreshButtonSize.X + applyButtonSize.X + addButtonSize.X + (ImGui.GetStyle().ItemSpacing.X * 3.5f));

                        ImGui.SameLine();
                        ImGui.SetCursorPosX(offsetFromRight);
                        if (metaInfoDownloaded)
                        {
                            _uiSharedService.BooleanToColoredIcon(metaInfo != null, false);
                            if (metaInfo != null)
                            {
                                UiSharedService.AttachToolTip("Metainfo present" + UiSharedService.TooltipSeparator
                                    + $"Last Updated: {metaInfo!.UpdatedDate}" + Environment.NewLine
                                    + $"Description: {metaInfo!.Description}" + Environment.NewLine
                                    + $"Poses: {metaInfo!.PoseData.Count}");
                            }
                            else
                            {
                                UiSharedService.AttachToolTip("Metainfo could not be downloaded." + UiSharedService.TooltipSeparator
                                    + "The data associated with the code is either not present on the server anymore or you have no access to it");
                            }
                        }
                        else
                        {
                            _uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey);
                            UiSharedService.AttachToolTip("Unknown accessibility state. Click the button on the right to refresh.");
                        }

                        ImGui.SameLine();
                        bool isInTimeout = _charaDataManager.IsInTimeout(favorite.Key);
                        using (ImRaii.Disabled(isInTimeout))
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowsSpin))
                            {
                                _charaDataManager.DownloadMetaInfo(favorite.Key, false);
                                UpdateFilteredItems();
                            }
                        }
                        UiSharedService.AttachToolTip(isInTimeout ? "Timeout for refreshing active, please wait before refreshing again."
                            : "Refresh data for this entry from the Server.");

                        ImGui.SameLine();
                        GposeMetaInfoAction((meta) =>
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                            {
                                _ = _charaDataManager.ApplyCharaDataToGposeTarget(metaInfo!);
                            }
                        }, "Apply Character Data to GPose Target", metaInfo, _hasValidGposeTarget, false);
                        ImGui.SameLine();
                        GposeMetaInfoAction((meta) =>
                        {
                            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                            {
                                _ = _charaDataManager.SpawnAndApplyData(meta!);
                            }
                        }, "Spawn Actor with Brio and apply Character Data", metaInfo, _hasValidGposeTarget, true);

                        string uidText = string.Empty;
                        var uid = favorite.Key.Split(":")[0];
                        if (metaInfo != null)
                        {
                            uidText = metaInfo.Uploader.AliasOrUID;
                        }
                        else
                        {
                            uidText = uid;
                        }

                        var note = _serverConfigurationManager.GetNoteForUid(uid);
                        if (note != null)
                        {
                            uidText = $"{note} ({uidText})";
                        }
                        ImGui.TextUnformatted(uidText);

                        ImGui.TextUnformatted("Last Use: ");
                        ImGui.SameLine();
                        ImGui.TextUnformatted(favorite.Value.Favorite.LastDownloaded == DateTime.MaxValue ? "Never" : favorite.Value.Favorite.LastDownloaded.ToString());

                        var desc = favorite.Value.Favorite.CustomDescription;
                        ImGui.SetNextItemWidth(maxPos - xPos);
                        if (ImGui.InputTextWithHint("##desc", "Custom Description for Favorite", ref desc, 100))
                        {
                            favorite.Value.Favorite.CustomDescription = desc;
                            _configService.Save();
                        }

                        DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                    });

                    ImGuiHelpers.ScaledDummy(5);
                }

                if (_configService.Current.FavoriteCodes.Count == 0)
                {
                    UiSharedService.ColorTextWrapped("You have no favorites added. Add Favorites through the other tabs before you can use this tab.", ImGuiColors.DalamudYellow);
                }
            }
        }

        using (var byCodeTabItem = ImRaii.TabItem("Code"))
        {
            using var id = ImRaii.PushId("byCodeTab");
            if (byCodeTabItem)
            {
                using var child = ImRaii.Child("sharedWithYouByCode", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                DrawHelpFoldout("You can apply character data you have a code for in this tab. Provide the code in it's given format \"OwnerUID:DataId\" into the field below and click on " +
                                "\"Get Info from Code\". This will provide you basic information about the data behind the code. Afterwards select an actor in GPose and press on \"Download and apply to <actor>\"." + Environment.NewLine + Environment.NewLine
                                + "Description: as set by the owner of the code to give you more or additional information of what this code may contain." + Environment.NewLine
                                + "Last Update: the date and time the owner of the code has last updated the data." + Environment.NewLine
                                + "Is Downloadable: whether or not the code is downloadable and applicable. If the code is not downloadable, contact the owner so they can attempt to fix it." + Environment.NewLine + Environment.NewLine
                                + "To download a code the code requires correct access permissions to be set by the owner. If getting info from the code fails, contact the owner to make sure they set their Access Permissions for the code correctly.");

                ImGuiHelpers.ScaledDummy(5);
                ImGui.InputTextWithHint("##importCode", "Enter Data Code", ref _importCode, 100);
                using (ImRaii.Disabled(string.IsNullOrEmpty(_importCode)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Get Info from Code"))
                    {
                        _charaDataManager.DownloadMetaInfo(_importCode);
                    }
                }
                GposeMetaInfoAction((meta) =>
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"Download and Apply"))
                    {
                        _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta!);
                    }
                }, "Apply this Character Data to the current GPose actor", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, false);
                ImGui.SameLine();
                GposeMetaInfoAction((meta) =>
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, $"Download and Spawn"))
                    {
                        _ = _charaDataManager.SpawnAndApplyData(meta!);
                    }
                }, "Spawn a new Brio actor and apply this Character Data", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, true);
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                DrawAddOrRemoveFavorite(_charaDataManager.LastDownloadedMetaInfo);

                ImGui.NewLine();
                if (!_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false)
                {
                    UiSharedService.ColorTextWrapped("Downloading meta info. Please wait.", ImGuiColors.DalamudYellow);
                }
                if ((_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false) && !_charaDataManager.DownloadMetaInfoTask.Result.Success)
                {
                    UiSharedService.ColorTextWrapped(_charaDataManager.DownloadMetaInfoTask.Result.Result, ImGuiColors.DalamudRed);
                }

                using (ImRaii.Disabled(_charaDataManager.LastDownloadedMetaInfo == null))
                {
                    ImGuiHelpers.ScaledDummy(5);
                    var metaInfo = _charaDataManager.LastDownloadedMetaInfo;
                    ImGui.TextUnformatted("Description");
                    ImGui.SameLine(150);
                    UiSharedService.TextWrapped(string.IsNullOrEmpty(metaInfo?.Description) ? "-" : metaInfo.Description);
                    ImGui.TextUnformatted("Last Update");
                    ImGui.SameLine(150);
                    ImGui.TextUnformatted(metaInfo?.UpdatedDate.ToLocalTime().ToString() ?? "-");
                    ImGui.TextUnformatted("Is Downloadable");
                    ImGui.SameLine(150);
                    _uiSharedService.BooleanToColoredIcon(metaInfo?.CanBeDownloaded ?? false, inline: false);
                    ImGui.TextUnformatted("Poses");
                    ImGui.SameLine(150);
                    if (metaInfo?.HasPoses ?? false)
                        DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                    else
                        _uiSharedService.BooleanToColoredIcon(false, false);
                }
            }
        }

        using (var yourOwnTabItem = ImRaii.TabItem("Your Own"))
        {
            using var id = ImRaii.PushId("yourOwnTab");
            if (yourOwnTabItem)
            {
                DrawHelpFoldout("You can apply character data you created yourself in this tab. If the list is not populated press on \"Download your Character Data\"." + Environment.NewLine + Environment.NewLine
                                 + "To create new and edit your existing character data use the \"MCD Online\" tab.");

                ImGuiHelpers.ScaledDummy(5);

                using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
                    || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data"))
                    {
                        _ = _charaDataManager.GetAllData(_disposalCts.Token);
                    }
                }
                if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
                {
                    UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();

                using var child = ImRaii.Child("ownDataChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                using var indent = ImRaii.PushIndent(10f);
                foreach (var data in _charaDataManager.OwnCharaData.Values)
                {
                    var hasMetaInfo = _charaDataManager.TryGetMetaInfo(data.FullId, out var metaInfo);
                    if (!hasMetaInfo) continue;
                    DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, metaInfo!, true);
                }

                ImGuiHelpers.ScaledDummy(5);
            }
        }

        using (var sharedWithYouTabItem = ImRaii.TabItem("Shared With You", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
        {
            using var id = ImRaii.PushId("sharedWithYouTab");
            if (sharedWithYouTabItem)
            {
                DrawHelpFoldout("You can apply character data shared with you implicitly in this tab. Shared Character Data are Character Data entries that have \"Sharing\" set to \"Shared\" and you have access through those by meeting the access restrictions, " +
                                "i.e. you were specified by your UID to gain access or are paired with the other user according to the Access Restrictions setting." + Environment.NewLine + Environment.NewLine
                                + "Filter if needed to find a specific entry, then just press on \"Apply to <actor>\" and it will download and apply the Character Data to the currently targeted GPose actor." + Environment.NewLine + Environment.NewLine
                                + "Note: Shared Data of Pairs you have paused will not be shown here.");

                ImGuiHelpers.ScaledDummy(5);

                DrawUpdateSharedDataButton();

                int activeFilters = 0;
                if (!string.IsNullOrEmpty(_sharedWithYouOwnerFilter)) activeFilters++;
                if (!string.IsNullOrEmpty(_sharedWithYouDescriptionFilter)) activeFilters++;
                if (_sharedWithYouDownloadableFilter) activeFilters++;
                string filtersText = activeFilters == 0 ? "Filters" : $"Filters ({activeFilters} active)";
                UiSharedService.DrawTree($"{filtersText}##filters", () =>
                {
                    var filterWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    ImGui.SetNextItemWidth(filterWidth);
                    if (ImGui.InputTextWithHint("##filter", "Filter by UID/Note", ref _sharedWithYouOwnerFilter, 30))
                    {
                        UpdateFilteredItems();
                    }
                    ImGui.SetNextItemWidth(filterWidth);
                    if (ImGui.InputTextWithHint("##filterDesc", "Filter by Description", ref _sharedWithYouDescriptionFilter, 50))
                    {
                        UpdateFilteredItems();
                    }
                    if (ImGui.Checkbox("Only show downloadable", ref _sharedWithYouDownloadableFilter))
                    {
                        UpdateFilteredItems();
                    }
                });

                if (_filteredDict == null && _charaDataManager.GetSharedWithYouTask == null)
                {
                    _filteredDict = _charaDataManager.SharedWithYouData
                        .ToDictionary(k =>
                        {
                            var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
                            if (note == null) return k.Key.AliasOrUID;
                            return $"{note} ({k.Key.AliasOrUID})";
                        }, k => k.Value, StringComparer.OrdinalIgnoreCase)
                        .Where(k => string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter))
                        .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
                }

                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
                using var child = ImRaii.Child("sharedWithYouChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

                ImGuiHelpers.ScaledDummy(5);
                foreach (var entry in _filteredDict ?? [])
                {
                    bool isFilteredAndHasToBeOpened = entry.Key.Contains(_sharedWithYouOwnerFilter) && _openDataApplicationShared;
                    if (isFilteredAndHasToBeOpened)
                        ImGui.SetNextItemOpen(isFilteredAndHasToBeOpened);
                    UiSharedService.DrawTree($"{entry.Key} - [{entry.Value.Count} Character Data Sets]##{entry.Key}", () =>
                    {
                        foreach (var data in entry.Value)
                        {
                            DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, data);
                        }
                        ImGuiHelpers.ScaledDummy(5);
                    });
                    if (isFilteredAndHasToBeOpened)
                        _openDataApplicationShared = false;
                }
            }
        }

        using (var mcdfTabItem = ImRaii.TabItem("From MCDF"))
        {
            using var id = ImRaii.PushId("applyMcdfTab");
            if (mcdfTabItem)
            {
                using var child = ImRaii.Child("applyMcdf", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                DrawHelpFoldout("You can apply character data shared with you using a MCDF file in this tab." + Environment.NewLine + Environment.NewLine
                                + "Load the MCDF first via the \"Load MCDF\" button which will give you the basic description that the owner has set during export." + Environment.NewLine
                                + "You can then apply it to any handled GPose actor." + Environment.NewLine + Environment.NewLine
                                + "MCDF to share with others can be generated using the \"MCDF Export\" tab at the top.");

                ImGuiHelpers.ScaledDummy(5);

                if (_charaDataManager.LoadedMcdfHeader == null || _charaDataManager.LoadedMcdfHeader.IsCompleted)
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF"))
                    {
                        _fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", (success, paths) =>
                        {
                            if (!success) return;
                            if (paths.FirstOrDefault() is not string path) return;

                            _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                            _configService.Save();

                            _charaDataManager.LoadMcdf(path);
                        }, 1, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
                    }
                    UiSharedService.AttachToolTip("Load MCDF Metadata into memory");
                    if ((_charaDataManager.LoadedMcdfHeader?.IsCompleted ?? false))
                    {
                        ImGui.TextUnformatted("Loaded file");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.FilePath);
                        ImGui.Text("Description");
                        ImGui.SameLine(200);
                        UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.CharaFileData.Description);

                        ImGuiHelpers.ScaledDummy(5);

                        using (ImRaii.Disabled(!_hasValidGposeTarget))
                        {
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply"))
                            {
                                _ = _charaDataManager.McdfApplyToGposeTarget();
                            }
                            UiSharedService.AttachToolTip($"Apply to {_gposeTarget}");
                            ImGui.SameLine();
                            using (ImRaii.Disabled(!_charaDataManager.BrioAvailable))
                            {
                                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Spawn Actor and Apply"))
                                {
                                    _charaDataManager.McdfSpawnApplyToGposeTarget();
                                }
                            }
                        }
                    }
                    if ((_charaDataManager.LoadedMcdfHeader?.IsFaulted ?? false) || (_charaDataManager.McdfApplicationTask?.IsFaulted ?? false))
                    {
                        UiSharedService.ColorTextWrapped("Failure to read MCDF file. MCDF file is possibly corrupt. Re-export the MCDF file and try again.",
                            ImGuiColors.DalamudRed);
                        UiSharedService.ColorTextWrapped("Note: if this is your MCDF, try redrawing yourself, wait and re-export the file. " +
                            "If you received it from someone else have them do the same.", ImGuiColors.DalamudYellow);
                    }
                }
                else
                {
                    UiSharedService.ColorTextWrapped("Loading Character...", ImGuiColors.DalamudYellow);
                }
            }
        }
    }

    private void DrawMcdfExport()
    {
        _uiSharedService.BigText("Mare Character Data File Export");

        DrawHelpFoldout("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files can officially only be imported during GPose through Mare. " +
            "Be aware that the possibility exists that people write unofficial custom exporters to extract the containing data.");

        ImGuiHelpers.ScaledDummy(5);

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that by exporting my character data into a file and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
            {
                string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                    ? "export.mcdf"
                    : string.Join('_', $"{_exportDescription}.mcdf".Split(Path.GetInvalidFileNameChars()));
                _uiSharedService.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                {
                    if (!success) return;

                    _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    _charaDataManager.SaveMareCharaFile(_exportDescription, path);
                    _exportDescription = string.Empty;
                }, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
            }
            UiSharedService.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance" +
                " equipped and redraw your character before exporting.", ImGuiColors.DalamudYellow);

            ImGui.Unindent();
        }
    }

    private void DrawMetaInfoData(string selectedGposeActor, bool hasValidGposeTarget, CharaDataMetaInfoExtendedDto data, bool canOpen = false)
    {
        ImGuiHelpers.ScaledDummy(5);
        using var entryId = ImRaii.PushId(data.FullId);

        var startPos = ImGui.GetCursorPosX();
        var maxPos = ImGui.GetWindowContentRegionMax().X;
        var availableWidth = maxPos - startPos;
        UiSharedService.DrawGrouped(() =>
        {
            ImGui.AlignTextToFramePadding();
            DrawAddOrRemoveFavorite(data);

            ImGui.SameLine();
            var favPos = ImGui.GetCursorPosX();
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorText(data.FullId, UiSharedService.GetBoolColor(data.CanBeDownloaded));
            if (!data.CanBeDownloaded)
            {
                UiSharedService.AttachToolTip("This data is incomplete on the server and cannot be downloaded. Contact the owner so they can fix it. If you are the owner, review the data in the MCD Online tab.");
            }

            var offsetFromRight = availableWidth - _uiSharedService.GetIconSize(FontAwesomeIcon.Calendar).X - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X
                - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X - ImGui.GetStyle().ItemSpacing.X * 2;

            ImGui.SameLine();
            ImGui.SetCursorPosX(offsetFromRight);
            _uiSharedService.IconText(FontAwesomeIcon.Calendar);
            UiSharedService.AttachToolTip($"Last Update: {data.UpdatedDate}");

            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                {
                    _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta!);
                }
            }, $"Apply Character data to {CharaName(selectedGposeActor)}", data, hasValidGposeTarget, false);
            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _charaDataManager.SpawnAndApplyData(meta!);
                }
            }, "Spawn and Apply Character data", data, hasValidGposeTarget, true);

            using var indent = ImRaii.PushIndent(favPos - startPos);

            if (canOpen)
            {
                using (ImRaii.Disabled(_isHandlingSelf))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Edit, "Open in MCD Online Editor"))
                    {
                        SelectedDtoId = data.Id;
                        _openMcdOnlineOnNextRun = true;
                    }
                }
                if (_isHandlingSelf)
                {
                    UiSharedService.AttachToolTip("Cannot use MCD Online while having Character Data applied to self.");
                }
            }

            if (string.IsNullOrEmpty(data.Description))
            {
                UiSharedService.ColorTextWrapped("No description set", ImGuiColors.DalamudGrey, availableWidth);
            }
            else
            {
                UiSharedService.TextWrapped(data.Description, availableWidth);
            }

            DrawPoseData(data, selectedGposeActor, hasValidGposeTarget);
        });
    }


    private void DrawPoseData(CharaDataMetaInfoExtendedDto? metaInfo, string actor, bool hasValidGposeTarget)
    {
        if (metaInfo == null || !metaInfo.HasPoses) return;

        bool isInGpose = _uiSharedService.IsInGpose;
        var start = ImGui.GetCursorPosX();
        foreach (var item in metaInfo.PoseExtended)
        {
            if (!item.HasPoseData) continue;

            float DrawIcon(float s)
            {
                ImGui.SetCursorPosX(s);
                var posX = ImGui.GetCursorPosX();
                _uiSharedService.IconText(item.HasWorldData ? FontAwesomeIcon.Circle : FontAwesomeIcon.Running);
                if (item.HasWorldData)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    using var col = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.WindowBg));
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                }
                ImGui.SameLine();
                return ImGui.GetCursorPosX();
            }

            string tooltip = string.IsNullOrEmpty(item.Description) ? "No description set" : "Pose Description: " + item.Description;
            if (!isInGpose)
            {
                start = DrawIcon(start);
                UiSharedService.AttachToolTip(tooltip + UiSharedService.TooltipSeparator + (item.HasWorldData ? GetWorldDataTooltipText(item) + UiSharedService.TooltipSeparator + "Click to show on Map" : string.Empty));
                if (item.HasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(item.Position, item.Map);
                }
            }
            else
            {
                tooltip += UiSharedService.TooltipSeparator + $"Left Click: Apply this pose to {CharaName(actor)}";
                if (item.HasWorldData) tooltip += Environment.NewLine + $"CTRL+Right Click: Apply world position to {CharaName(actor)}."
                        + UiSharedService.TooltipSeparator + "!!! CAUTION: Applying world position will likely yeet this actor into nirvana. Use at your own risk !!!";
                GposePoseAction(() =>
                {
                    start = DrawIcon(start);
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        _ = _charaDataManager.ApplyPoseData(item, actor);
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && UiSharedService.CtrlPressed())
                    {
                        _ = _charaDataManager.ApplyWorldDataToTarget(item, actor);
                    }
                }, tooltip, hasValidGposeTarget);
                ImGui.SameLine();
            }
        }
        if (metaInfo.PoseExtended.Any()) ImGui.NewLine();
    }

    private void DrawSettings()
    {
        ImGuiHelpers.ScaledDummy(5);
        _uiSharedService.BigText("Settings");
        ImGuiHelpers.ScaledDummy(5);
        bool openInGpose = _configService.Current.OpenMareHubOnGposeStart;
        if (ImGui.Checkbox("Open Character Data Hub when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenMareHubOnGposeStart = openInGpose;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /mare gpose");
        bool downloadDataOnConnection = _configService.Current.DownloadMcdDataOnConnection;
        if (ImGui.Checkbox("Download MCD Online Data on connecting", ref downloadDataOnConnection))
        {
            _configService.Current.DownloadMcdDataOnConnection = downloadDataOnConnection;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically download MCD Online data (Your Own and Shared with You) once a connection is established to the server.");

        bool showHelpTexts = _configService.Current.ShowHelpTexts;
        if (ImGui.Checkbox("Show \"What is this? (Explanation / Help)\" foldouts", ref showHelpTexts))
        {
            _configService.Current.ShowHelpTexts = showHelpTexts;
            _configService.Save();
        }

        ImGui.Checkbox("Abbreviate Chara Names", ref _abbreviateCharaName);
        _uiSharedService.DrawHelpText("This setting will abbreviate displayed names. This setting is not persistent and will reset between restarts.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Last Export Folder");
        ImGui.SameLine(300);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(string.IsNullOrEmpty(_configService.Current.LastSavedCharaDataLocation) ? "Not set" : _configService.Current.LastSavedCharaDataLocation);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear Last Export Folder"))
        {
            _configService.Current.LastSavedCharaDataLocation = string.Empty;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("Use this if the Load or Save MCDF file dialog does not open");
    }

    private void DrawHelpFoldout(string text)
    {
        if (_configService.Current.ShowHelpTexts)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawTree("What is this? (Explanation / Help)", () =>
            {
                UiSharedService.TextWrapped(text);
            });
        }
    }

    private void DisableDisabled(Action drawAction)
    {
        if (_disableUI) ImGui.EndDisabled();
        drawAction();
        if (_disableUI) ImGui.BeginDisabled();
    }
}