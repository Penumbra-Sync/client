using System.Diagnostics;
using System.Threading;
using Dalamud.Game.ClientState;
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
        private readonly ClientState _clientState;
        private readonly IpcManager _ipcManager;
        private readonly FileReplacementFactory _factory;

        public CharacterDataFactory(ClientState clientState, IpcManager ipcManager, FileReplacementFactory factory)
        {
            Logger.Debug("Creating " + nameof(CharacterDataFactory));

            _clientState = clientState;
            _ipcManager = ipcManager;
            _factory = factory;
        }

        private string GetPlayerName()
        {
            return _clientState.LocalPlayer!.Name.ToString();
        }

        public unsafe CharacterData BuildCharacterData()
        {
            Stopwatch st = Stopwatch.StartNew();
            var cache = new CharacterData();

            while (_clientState.LocalPlayer == null)
            {
                Logger.Debug("Character is null but it shouldn't be, waiting");
                Thread.Sleep(50);
            }
            var model = (CharacterBase*)((Character*)_clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();
            for (var idx = 0; idx < model->SlotCount; ++idx)
            {
                var mdl = (RenderModel*)model->ModelArray[idx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement cachedMdlResource = _factory.Create();
                cachedMdlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mdlPath, GetPlayerName());
                cachedMdlResource.SetResolvedPath(mdlPath);
                //PluginLog.Verbose("Resolving for model " + mdlPath);

                cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    //var mtrlFileResource = factory.Create();
                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];
                    //PluginLog.Verbose("Resolving for material " + mtrlPath);
                    var cachedMtrlResource = _factory.Create();
                    cachedMtrlResource.GamePaths = _ipcManager.PenumbraReverseResolvePath(mtrlPath, GetPlayerName());
                    cachedMtrlResource.SetResolvedPath(mtrlPath);
                    cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResource->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath.ToString())) continue;

                        var cachedTexResource = _factory.Create();
                        cachedTexResource.GamePaths = new[] { texPath };
                        cachedTexResource.SetResolvedPath(_ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!);
                        if (!cachedTexResource.HasFileReplacement)
                        {
                            // try resolving tex with -- in name instead
                            texPath = texPath.Insert(texPath.LastIndexOf('/') + 1, "--");
                            var reResolvedPath = _ipcManager.PenumbraResolvePath(texPath, GetPlayerName())!;
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

            cache.GlamourerString = _ipcManager.GlamourerGetCharacterCustomization()!;
            cache.ManipulationString = _ipcManager.PenumbraGetMetaManipulations(_clientState.LocalPlayer!.Name.ToString());
            cache.JobId = _clientState.LocalPlayer!.ClassJob.Id;

            st.Stop();
            Logger.Debug("Building Character Data took " + st.Elapsed);

            return cache;
        }
    }
}
