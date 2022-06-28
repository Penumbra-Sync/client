using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        private FileReplacement CreateFileReplacement(string path)
        {
            var fileReplacement = new FileReplacement(_ipcManager.PenumbraModDirectory()!);
            if (!path.Contains(".tex", StringComparison.OrdinalIgnoreCase))
            {
                fileReplacement.GamePaths =
                    _ipcManager.PenumbraReverseResolvePath(path, _dalamudUtil.PlayerName).ToList();
                fileReplacement.SetResolvedPath(path);
            }
            else
            {
                fileReplacement.GamePaths = new List<string> { path };
                fileReplacement.SetResolvedPath(_ipcManager.PenumbraResolvePath(path, _dalamudUtil.PlayerName)!);
                if (!fileReplacement.HasFileReplacement)
                {
                    // try resolving tex with -- in name instead
                    path = path.Insert(path.LastIndexOf('/') + 1, "--");
                    var reResolvedPath = _ipcManager.PenumbraResolvePath(path, _dalamudUtil.PlayerName)!;
                    if (reResolvedPath != path)
                    {
                        fileReplacement.GamePaths = new List<string>() { path };
                        fileReplacement.SetResolvedPath(reResolvedPath);
                    }
                }
            }

            return fileReplacement;
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
            for (var mdlIdx = 0; mdlIdx < model->SlotCount; ++mdlIdx)
            {
                var mdl = (RenderModel*)model->ModelArray[mdlIdx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement mdlFileReplacement = CreateFileReplacement(mdlPath);
                Logger.Verbose("Model " + string.Join(", ", mdlFileReplacement.GamePaths));
                Logger.Verbose("\t\t=> " + mdlFileReplacement.ResolvedPath);

                cache.AddFileReplacement(mdlFileReplacement);

                for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];
                    var mtrlFileReplacement = CreateFileReplacement(mtrlPath);
                    Logger.Verbose("\tMaterial " + string.Join(", ", mtrlFileReplacement.GamePaths));
                    Logger.Verbose("\t\t\t=> " + mtrlFileReplacement.ResolvedPath);
                    cache.AddFileReplacement(mtrlFileReplacement);

                    var mtrlResourceHandle = (MtrlResource*)mtrl->ResourceHandle;
                    for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResourceHandle->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath)) continue;

                        var texFileReplacement = CreateFileReplacement(texPath);
                        Logger.Verbose("\t\tTexture " + string.Join(", ", texFileReplacement.GamePaths));
                        Logger.Verbose("\t\t\t\t=> " + texFileReplacement.ResolvedPath);
                        cache.AddFileReplacement(texFileReplacement);
                    }
                }
            }

            st.Stop();
            Logger.Verbose("Building Character Data took " + st.Elapsed);

            return cache;
        }
    }
}
