﻿using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Internal.Notifications;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI.Files.Models;
using System.Numerics;

namespace MareSynchronos.Services.Mediator;

#pragma warning disable MA0048 // File name must match type name
public record SwitchToIntroUiMessage : IMessage;
public record SwitchToMainUiMessage : IMessage;
public record OpenSettingsUiMessage : IMessage;
public record DalamudLoginMessage : IMessage;
public record DalamudLogoutMessage : IMessage;
public record FrameworkUpdateMessage : IMessage;
public record ClassJobChangedMessage(uint? ClassJob) : IMessage;
public record DelayedFrameworkUpdateMessage : IMessage;
public record ZoneSwitchStartMessage : IMessage;
public record ZoneSwitchEndMessage : IMessage;
public record CutsceneStartMessage : IMessage;
public record GposeStartMessage : IMessage;
public record GposeEndMessage : IMessage;
public record CutsceneEndMessage : IMessage;
public record CutsceneFrameworkUpdateMessage : IMessage;
public record ConnectedMessage(ConnectionDto Connection) : IMessage;
public record DisconnectedMessage : IMessage;
public record PenumbraModSettingChangedMessage : IMessage;
public record PenumbraInitializedMessage : IMessage;
public record PenumbraDisposedMessage : IMessage;
public record PenumbraRedrawMessage(IntPtr Address, int ObjTblIdx, bool WasRequested) : IMessage;
public record HeelsOffsetMessage : IMessage;
public record PenumbraResourceLoadMessage(IntPtr GameObject, string GamePath, string FilePath) : IMessage;
public record CustomizePlusMessage : IMessage;
public record PalettePlusMessage(Character Character) : IMessage;
public record HonorificMessage : IMessage;
public record PlayerChangedMessage(API.Data.CharacterData Data) : IMessage;
public record CharacterChangedMessage(GameObjectHandler GameObjectHandler) : IMessage;
public record TransientResourceChangedMessage(IntPtr Address) : IMessage;
public record AddWatchedGameObjectHandler(GameObjectHandler Handler) : IMessage;
public record RemoveWatchedGameObjectHandler(GameObjectHandler Handler) : IMessage;
public record HaltScanMessage(string Source) : IMessage;
public record ResumeScanMessage(string Source) : IMessage;
public record NotificationMessage
    (string Title, string Message, NotificationType Type, uint TimeShownOnScreen = 3000) : IMessage;
public record CreateCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : IMessage;
public record ClearCacheForObjectMessage(GameObjectHandler ObjectToCreateFor) : IMessage;
public record CharacterDataCreatedMessage(API.Data.CharacterData CharacterData) : IMessage;
public record PenumbraStartRedrawMessage(IntPtr Address) : IMessage;
public record PenumbraEndRedrawMessage(IntPtr Address) : IMessage;
public record HubReconnectingMessage(Exception? Exception) : IMessage;
public record HubReconnectedMessage(string? Arg) : IMessage;
public record HubClosedMessage(Exception? Exception) : IMessage;
public record DownloadReadyMessage(Guid RequestId) : IMessage;
public record DownloadStartedMessage(GameObjectHandler DownloadId, Dictionary<string, FileDownloadStatus> DownloadStatus) : IMessage;
public record DownloadFinishedMessage(GameObjectHandler DownloadId) : IMessage;
public record UiToggleMessage(Type UiType) : IMessage;
public record PlayerUploadingMessage(GameObjectHandler Handler, bool IsUploading) : IMessage;
public record ClearProfileDataMessage(UserData? UserData = null) : IMessage;
public record CyclePauseMessage(UserData UserData) : IMessage;
public record ProfilePopoutToggle(Pair? Pair) : IMessage;
public record CompactUiChange(Vector2 Size, Vector2 Position) : IMessage;
public record ProfileOpenStandaloneMessage(Pair Pair) : IMessage;
public record RemoveWindowMessage(WindowMediatorSubscriberBase Window) : IMessage;

#pragma warning restore MA0048 // File name must match type name