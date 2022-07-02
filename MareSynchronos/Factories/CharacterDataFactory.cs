using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Managers;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;
using Human = MareSynchronos.Interop.Human;

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

        private FileReplacement CreateFileReplacement(string path, bool doNotReverseResolve = false)
        {
            var fileReplacement = new FileReplacement(_ipcManager.PenumbraModDirectory()!);
            if (!doNotReverseResolve)
            {
                fileReplacement.GamePaths =
                    _ipcManager.PenumbraReverseResolvePath(path, _dalamudUtil.PlayerName).ToList();
                fileReplacement.SetResolvedPath(path);
            }
            else
            {
                fileReplacement.GamePaths = new List<string> { path };
                fileReplacement.SetResolvedPath(_ipcManager.PenumbraResolvePath(path, _dalamudUtil.PlayerName)!);
            }

            return fileReplacement;
        }

        public CharacterData BuildCharacterData()
        {
            if (!_ipcManager.Initialized)
            {
                throw new ArgumentException("Penumbra is not connected");
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
            _dalamudUtil.WaitWhileCharacterIsDrawing(_dalamudUtil.PlayerPointer);
            var cache = new CharacterData
            {
                JobId = _dalamudUtil.PlayerJobId,
                GlamourerString = _ipcManager.GlamourerGetCharacterCustomization(_dalamudUtil.PlayerCharacter),
                ManipulationString = _ipcManager.PenumbraGetMetaManipulations(_dalamudUtil.PlayerName)
            };
            var human = (Human*)((Character*)_dalamudUtil.PlayerPointer)->GameObject.GetDrawObject();
            for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
            {
                var mdl = (RenderModel*)human->CharacterBase.ModelArray[mdlIdx];
                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();

                FileReplacement mdlFileReplacement = CreateFileReplacement(mdlPath);
                Logger.Debug("Model " + string.Join(", ", mdlFileReplacement.GamePaths));
                Logger.Debug("\t\t=> " + mdlFileReplacement.ResolvedPath);

                cache.AddFileReplacement(mdlFileReplacement);

                for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];
                    if (mtrl == null) continue;

                    var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];

                    var mtrlFileReplacement = CreateFileReplacement(mtrlPath);
                    Logger.Debug("\tMaterial " + string.Join(", ", mtrlFileReplacement.GamePaths));
                    Logger.Debug("\t\t\t=> " + mtrlFileReplacement.ResolvedPath);

                    cache.AddFileReplacement(mtrlFileReplacement);

                    var mtrlResourceHandle = (MtrlResource*)mtrl->ResourceHandle;
                    for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
                    {
                        var texPath = new Utf8String(mtrlResourceHandle->TexString(resIdx)).ToString();

                        if (string.IsNullOrEmpty(texPath)) continue;

                        var texFileReplacement = CreateFileReplacement(texPath, true);
                        Logger.Debug("\t\tTexture " + string.Join(", ", texFileReplacement.GamePaths));
                        Logger.Debug("\t\t\t\t=> " + texFileReplacement.ResolvedPath);

                        cache.AddFileReplacement(texFileReplacement);

                        if (texPath.Contains("/--")) continue;

                        var texDoubleMinusFileReplacement =
                            CreateFileReplacement(texPath.Insert(texPath.LastIndexOf('/') + 1, "--"), true);

                        Logger.Debug("\t\tTexture-- " + string.Join(", ", texDoubleMinusFileReplacement.GamePaths));
                        Logger.Debug("\t\t\t\t=> " + texDoubleMinusFileReplacement.ResolvedPath);
                        cache.AddFileReplacement(texDoubleMinusFileReplacement);
                    }
                }
            }

            var weapon = (RenderModel*)human->Weapon->WeaponRenderModel->RenderModel;

            var weaponPath = new Utf8String(weapon->ResourceHandle->FileName()).ToString();
            FileReplacement weaponReplacement = CreateFileReplacement(weaponPath);
            cache.AddFileReplacement(weaponReplacement);

            Logger.Debug("Weapon " + string.Join(", ", weaponReplacement.GamePaths));
            Logger.Debug("\t\t=> " + weaponReplacement.ResolvedPath);
            
            for (var mtrlIdx = 0; mtrlIdx < weapon->MaterialCount; mtrlIdx++)
            {
                var mtrl = (Material*)weapon->Materials[mtrlIdx];
                if (mtrl == null) continue;

                var mtrlPath = new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2];

                var mtrlFileReplacement = CreateFileReplacement(mtrlPath);
                Logger.Debug("\tWeapon Material " + string.Join(", ", mtrlFileReplacement.GamePaths));
                Logger.Debug("\t\t\t=> " + mtrlFileReplacement.ResolvedPath);

                cache.AddFileReplacement(mtrlFileReplacement);

                var mtrlResourceHandle = (MtrlResource*)mtrl->ResourceHandle;
                for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
                {
                    var texPath = new Utf8String(mtrlResourceHandle->TexString(resIdx)).ToString();

                    if (string.IsNullOrEmpty(texPath)) continue;

                    var texFileReplacement = CreateFileReplacement(texPath, true);
                    Logger.Debug("\t\tWeapon Texture " + string.Join(", ", texFileReplacement.GamePaths));
                    Logger.Debug("\t\t\t\t=> " + texFileReplacement.ResolvedPath);

                    cache.AddFileReplacement(texFileReplacement);

                    if (texPath.Contains("/--")) continue;

                    var texDoubleMinusFileReplacement =
                        CreateFileReplacement(texPath.Insert(texPath.LastIndexOf('/') + 1, "--"), true);

                    Logger.Debug("\t\tWeapon Texture-- " + string.Join(", ", texDoubleMinusFileReplacement.GamePaths));
                    Logger.Debug("\t\t\t\t=> " + texDoubleMinusFileReplacement.ResolvedPath);
                    cache.AddFileReplacement(texDoubleMinusFileReplacement);
                }
            }

            var tattooDecalFileReplacement =
                CreateFileReplacement(new Utf8String(human->Decal->FileName()).ToString());
            cache.AddFileReplacement(tattooDecalFileReplacement);
            Logger.Debug("Decal " + string.Join(", ", tattooDecalFileReplacement.GamePaths));
            Logger.Debug("\t\t=> " + tattooDecalFileReplacement.ResolvedPath);

            var legacyDecalFileReplacement =
                CreateFileReplacement(new Utf8String(human->LegacyBodyDecal->FileName()).ToString());
            cache.AddFileReplacement(legacyDecalFileReplacement);
            Logger.Debug("Legacy Decal " + string.Join(", ", legacyDecalFileReplacement.GamePaths));
            Logger.Debug("\t\t=> " + legacyDecalFileReplacement.ResolvedPath);

            st.Stop();
            Logger.Verbose("Building Character Data took " + st.Elapsed);

            return cache;
        }
    }
}
