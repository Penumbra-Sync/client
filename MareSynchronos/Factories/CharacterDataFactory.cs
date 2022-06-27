using System;
using System.Diagnostics;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Managers;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;

namespace MareSynchronos.Factories
{
    public class CharacterDataFactory
    {
        private readonly DalamudUtil _dalamudUtil;
        private readonly IpcManager _ipcManager;

        public CharacterDataFactory(DalamudUtil dalamudUtil, IpcManager ipcManager)
        {
            Logger.Debug("Creating " + nameof(CharacterDataFactory));

            _dalamudUtil = dalamudUtil;
            _ipcManager = ipcManager;
        }

        private FileReplacement CreateBaseFileReplacement()
        {
            return new FileReplacement(_ipcManager.PenumbraModDirectory()!);
        }

        public CharacterData BuildCharacterData()
        {
            if (!_dalamudUtil.IsPlayerPresent || !_ipcManager.Initialized)
            {
                throw new ArgumentException("Player is not present or Penumbra is not connected");
            }

            return CreateCharacterData();
        }

        private unsafe CharacterData CreateCharacterData()
        {
            Stopwatch st = Stopwatch.StartNew();
            while (!_dalamudUtil.IsPlayerPresent)
            {
                Logger.Debug("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }
            var cache = new CharacterData
            {
                JobId = _dalamudUtil.PlayerJobId,
                GlamourerString = _ipcManager.GlamourerGetCharacterCustomization(_dalamudUtil.PlayerName),
                ManipulationString = _ipcManager.PenumbraGetMetaManipulations(_dalamudUtil.PlayerName)
            };
            var model = (CharacterBase*)((Character*)_dalamudUtil.PlayerPointer)->GameObject.GetDrawObject();
            for (var idx = 0; idx < model->SlotCount; ++idx)
            {
                var mdl = (RenderModel*)model->ModelArray[idx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement cachedMdlResource = CreateBaseFileReplacement();
                cachedMdlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mdlPath, _dalamudUtil.PlayerName);
                cachedMdlResource.SetResolvedPath(mdlPath);
                //PluginLog.Verbose("Resolving for model " + mdlPath);

                cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    //var mtrlFileResource = factory.CreateBaseFileReplacement();
                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];
                    //PluginLog.Verbose("Resolving for material " + mtrlPath);
                    var cachedMtrlResource = CreateBaseFileReplacement();
                    cachedMtrlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mtrlPath, _dalamudUtil.PlayerName);
                    cachedMtrlResource.SetResolvedPath(mtrlPath);
                    cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResource->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath.ToString())) continue;

                        var cachedTexResource = CreateBaseFileReplacement();
                        cachedTexResource.GamePaths = new[] { texPath };
                        cachedTexResource.SetResolvedPath(_ipcManager.PenumbraResolvePath(texPath, _dalamudUtil.PlayerName)!);
                        if (!cachedTexResource.HasFileReplacement)
                        {
                            // try resolving tex with -- in name instead
                            texPath = texPath.Insert(texPath.LastIndexOf('/') + 1, "--");
                            var reResolvedPath = _ipcManager.PenumbraResolvePath(texPath, _dalamudUtil.PlayerName)!;
                            if (reResolvedPath != texPath)
                            {
                                cachedTexResource.GamePaths = new[] { texPath };
                                cachedTexResource.SetResolvedPath(reResolvedPath);
                            }
                        }
                        cache.AddAssociatedResource(cachedTexResource, cachedMdlResource, cachedMtrlResource);
                    }
                }
            }

            st.Stop();
            Logger.Debug("Building Character Data took " + st.Elapsed);

            return cache;
        }
    }
}
