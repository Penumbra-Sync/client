using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI.Files.Models;
using System.Numerics;

namespace MareSynchronos.Services.Mediator;

#pragma warning disable MA0048 // File name must match type name
public record SwitchToIntroUiMessage : MessageBase;
public record SwitchToMainUiMessage : MessageBase;
public record OpenSettingsUiMessage : MessageBase;
public record DalamudLoginMessage : MessageBase;
public record DalamudLogoutMessage : MessageBase;
public record FrameworkUpdateMessage : MessageBase
{
    public override bool KeepThreadContext => true;
}
public record ClassJobChangedMessage(uint? ClassJob) : MessageBase;
public record DelayedFrameworkUpdateMessage : MessageBase
{
    public override bool KeepThreadContext => true;
}
public record ZoneSwitchStartMessage : MessageBase;
public record ZoneSwitchEndMessage : MessageBase;
public record CutsceneStartMessage : MessageBase;
public record GposeStartMessage : MessageBase;
public record GposeEndMessage : MessageBase;
public record CutsceneEndMessage : MessageBase;
public record CutsceneFrameworkUpdateMessage : MessageBase
{
    public override bool KeepThreadContext => true;
}
public record ConnectedMessage(ConnectionDto Connection) : MessageBase;
public record DisconnectedMessage : MessageBase;
public record PenumbraModSettingChangedMessage : MessageBase;
public record PenumbraInitializedMessage : MessageBase;
public record PenumbraDisposedMessage : MessageBase;
public record PenumbraRedrawMessage(IntPtr Address, int ObjTblIdx, bool WasRequested) : MessageBase;
public record HeelsOffsetMessage : MessageBase;
public record PenumbraResourceLoadMessage(IntPtr GameObject, string GamePath, string FilePath) : MessageBase
{
    public override bool KeepThreadContext => true;
}
public record CustomizePlusMessage : MessageBase;
public record PalettePlusMessage(Character Character) : MessageBase;
public record HonorificMessage(string NewHonorificTitle) : MessageBase;
public record PlayerChangedMessage(API.Data.CharacterData Data) : MessageBase;
public record CharacterChangedMessage(GameObjectHandler GameObjectHandler) : MessageBase;
public record TransientResourceChangedMessage(IntPtr Address) : MessageBase;
public record AddWatchedGameObjectHandler(GameObjectHandler Handler) : MessageBase;
public record RemoveWatchedGameObjectHandler(GameObjectHandler Handler) : MessageBase;
public record HaltScanMessage(string Source) : MessageBase;
public record ResumeScanMessage(string Source) : MessageBase;
public record NotificationMessage
    (string Title, string Message, NotificationType Type, uint TimeShownOnScreen = 3000) : MessageBase;
public record CreateCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : MessageBase;
public record ClearCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : MessageBase;
public record CharacterDataCreatedMessage(API.Data.CharacterData CharacterData) : MessageBase;
public record PenumbraStartRedrawMessage(IntPtr Address) : MessageBase;
public record PenumbraEndRedrawMessage(IntPtr Address) : MessageBase;
public record HubReconnectingMessage(Exception? Exception) : MessageBase;
public record HubReconnectedMessage(string? Arg) : MessageBase;
public record HubClosedMessage(Exception? Exception) : MessageBase;
public record DownloadReadyMessage(Guid RequestId) : MessageBase;
public record DownloadStartedMessage(GameObjectHandler DownloadId, Dictionary<string, FileDownloadStatus> DownloadStatus) : MessageBase;
public record DownloadFinishedMessage(GameObjectHandler DownloadId) : MessageBase;
public record UiToggleMessage(Type UiType) : MessageBase;
public record PlayerUploadingMessage(GameObjectHandler Handler, bool IsUploading) : MessageBase;
public record ClearProfileDataMessage(UserData? UserData = null) : MessageBase;
public record CyclePauseMessage(UserData UserData) : MessageBase;
public record ProfilePopoutToggle(Pair? Pair) : MessageBase;
public record CompactUiChange(Vector2 Size, Vector2 Position) : MessageBase;
public record ProfileOpenStandaloneMessage(Pair Pair) : MessageBase;
public record RemoveWindowMessage(WindowMediatorSubscriberBase Window) : MessageBase;

#pragma warning restore MA0048 // File name must match type name