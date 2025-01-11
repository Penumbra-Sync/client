﻿using MareSynchronos.API.Data.Enum;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

internal sealed class CharaDataCharacterHandler : DisposableMediatorSubscriberBase
{
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;
    private readonly HashSet<HandledCharaDataEntry> _handledCharaData = [];

    public IEnumerable<HandledCharaDataEntry> HandledCharaData => _handledCharaData;

    public CharaDataCharacterHandler(ILogger<CharaDataCharacterHandler> logger, MareMediator mediator,
        GameObjectHandlerFactory gameObjectHandlerFactory, DalamudUtilService dalamudUtilService,
        IpcManager ipcManager)
        : base(logger, mediator)
    {
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        mediator.Subscribe<GposeEndMessage>(this, (_) =>
        {
            foreach (var chara in _handledCharaData)
            {
                RevertHandledChara(chara, false);
            }
        });

        mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleCutsceneFrameworkUpdate());
    }

    private void HandleCutsceneFrameworkUpdate()
    {
        if (!_dalamudUtilService.IsInGpose) return;

        foreach (var entry in _handledCharaData.ToList())
        {
            var chara = _dalamudUtilService.GetGposeCharacterFromObjectTableByName(entry.Name, onlyGposeCharacters: true);
            if (chara is null)
            {
                RevertChara(entry.Name, entry.CustomizePlus).GetAwaiter().GetResult();
                _handledCharaData.Remove(entry);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var chara in _handledCharaData)
        {
            RevertHandledChara(chara, false);
        }
    }

    public async Task RevertChara(string name, Guid? cPlusId, bool reapplyPose = true)
    {
        Guid applicationId = Guid.NewGuid();
        await _ipcManager.Glamourer.RevertByNameAsync(Logger, name, applicationId).ConfigureAwait(false);
        if (cPlusId != null)
        {
            await _ipcManager.CustomizePlus.RevertByIdAsync(cPlusId).ConfigureAwait(false);
        }
        using var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address != IntPtr.Zero)
        {
            var poseData = string.Empty;
            API.Dto.CharaData.WorldData? worldData = null;
            if (_dalamudUtilService.IsInGpose && reapplyPose)
            {
                poseData = await _ipcManager.Brio.GetPoseAsync(handler.Address).ConfigureAwait(false);
                worldData = await _ipcManager.Brio.GetTransformAsync(handler.Address).ConfigureAwait(false);
            }
            await _ipcManager.Penumbra.RedrawAsync(Logger, handler, applicationId, CancellationToken.None).ConfigureAwait(false);
            if (_dalamudUtilService.IsInGpose && reapplyPose)
            {
                await _ipcManager.Brio.SetPoseAsync(handler.Address, poseData ?? "{}").ConfigureAwait(false);
                await _ipcManager.Brio.ApplyTransformAsync(handler.Address, worldData!.Value).ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> RevertHandledChara(string name, bool reapplyPose = true)
    {
        var handled = _handledCharaData.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        if (handled == null) return false;
        _handledCharaData.Remove(handled);
        await _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(handled.Name, handled.CustomizePlus, reapplyPose)).ConfigureAwait(false);
        return true;
    }

    public Task RevertHandledChara(HandledCharaDataEntry? handled, bool reapplyPose = true)
    {
        if (handled == null) return Task.CompletedTask;
        _handledCharaData.Remove(handled);
        return _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(handled.Name, handled.CustomizePlus, reapplyPose));
    }

    internal void AddHandledChara(HandledCharaDataEntry handledCharaDataEntry)
    {
        _handledCharaData.Add(handledCharaDataEntry);
    }

    public void UpdateHandledData(Dictionary<string, CharaDataMetaInfoExtendedDto?> newData)
    {
        foreach (var handledData in _handledCharaData)
        {
            if (newData.TryGetValue(handledData.MetaInfo.FullId, out var metaInfo) && metaInfo != null)
            {
                handledData.MetaInfo = metaInfo;
            }
        }
    }

    public async Task<GameObjectHandler?> TryCreateGameObjectHandler(string name, bool gPoseOnly = false)
    {
        var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, gPoseOnly && _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address == nint.Zero) return null;
        return handler;
    }

    public async Task<GameObjectHandler?> TryCreateGameObjectHandler(int index)
    {
        var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(index)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address == nint.Zero) return null;
        return handler;
    }
}
