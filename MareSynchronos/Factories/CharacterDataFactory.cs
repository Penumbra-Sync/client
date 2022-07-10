using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Interop;
using MareSynchronos.Managers;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace MareSynchronos.Factories;
public class CharacterDataFactory
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;

    public CharacterDataFactory(DalamudUtil dalamudUtil, IpcManager ipcManager)
    {
        Logger.Verbose("Creating " + nameof(CharacterDataFactory));

        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
    }

    public CharacterData BuildCharacterData()
    {
        if (!_ipcManager.Initialized)
        {
            throw new ArgumentException("Penumbra is not connected");
        }

        return CreateCharacterData();
    }

    private (string, string) GetIndentationForInheritanceLevel(int inheritanceLevel)
    {
        return (string.Join("", Enumerable.Repeat("\t", inheritanceLevel)), string.Join("", Enumerable.Repeat("\t", inheritanceLevel + 2)));
    }

    private void DebugPrint(FileReplacement fileReplacement, string objectKind, string resourceType, int inheritanceLevel)
    {
        var indentation = GetIndentationForInheritanceLevel(inheritanceLevel);
        objectKind += string.IsNullOrEmpty(objectKind) ? "" : " ";

        Logger.Verbose(indentation.Item1 + objectKind + resourceType + " [" + string.Join(", ", fileReplacement.GamePaths) + "]");
        Logger.Verbose(indentation.Item2 + "=> " + fileReplacement.ResolvedPath);
    }

    private unsafe void AddReplacementsFromRenderModel(RenderModel* mdl, CharacterData cache, int inheritanceLevel = 0, string objectKind = "")
    {
        if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
        {
            return;
        }

        string mdlPath;
        try
        {
            mdlPath = new Utf8String(mdl->ResourceHandle->FileName()).ToString();
        }
        catch
        {
            Logger.Warn("Could not get model data for " + objectKind);
            return;
        }
        Logger.Verbose("Adding File Replacement for Model " + mdlPath);

        FileReplacement mdlFileReplacement = CreateFileReplacement(mdlPath);
        DebugPrint(mdlFileReplacement, objectKind, "Model", inheritanceLevel);

        cache.AddFileReplacement(mdlFileReplacement);

        for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
        {
            var mtrl = (Material*)mdl->Materials[mtrlIdx];
            if (mtrl == null) continue;

            AddReplacementsFromMaterial(mtrl, cache, inheritanceLevel + 1, objectKind);
        }
    }

    private unsafe void AddReplacementsFromMaterial(Material* mtrl, CharacterData cache, int inheritanceLevel = 0, string objectKind = "")
    {
        string fileName;
        try
        {
            fileName = new Utf8String(mtrl->ResourceHandle->FileName()).ToString();

        }
        catch
        {
            Logger.Warn("Could not get material data for " + objectKind);
            return;
        }

        Logger.Verbose("Adding File Replacement for Material " + fileName);
        var mtrlPath = fileName.Split("|")[2];

        var mtrlFileReplacement = CreateFileReplacement(mtrlPath);
        DebugPrint(mtrlFileReplacement, objectKind, "Material", inheritanceLevel);

        cache.AddFileReplacement(mtrlFileReplacement);

        var mtrlResourceHandle = (MtrlResource*)mtrl->ResourceHandle;
        for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
        {
            var texPath = new Utf8String(mtrlResourceHandle->TexString(resIdx)).ToString();

            if (string.IsNullOrEmpty(texPath)) continue;

            AddReplacementsFromTexture(texPath, cache, inheritanceLevel + 1, objectKind);
        }
    }

    private void AddReplacementsFromTexture(string texPath, CharacterData cache, int inheritanceLevel = 0, string objectKind = "", bool doNotReverseResolve = true)
    {
        if (texPath.IsNullOrEmpty()) return;

        Logger.Verbose("Adding File Replacement for Texture " + texPath);

        var texFileReplacement = CreateFileReplacement(texPath, doNotReverseResolve);
        DebugPrint(texFileReplacement, objectKind, "Texture", inheritanceLevel);

        cache.AddFileReplacement(texFileReplacement);

        if (texPath.Contains("/--")) return;

        var texDx11Replacement =
            CreateFileReplacement(texPath.Insert(texPath.LastIndexOf('/') + 1, "--"), doNotReverseResolve);

        DebugPrint(texDx11Replacement, objectKind, "Texture (DX11)", inheritanceLevel);

        cache.AddFileReplacement(texDx11Replacement);
    }

    private unsafe CharacterData CreateCharacterData()
    {
        Stopwatch st = Stopwatch.StartNew();
        while (!_dalamudUtil.IsPlayerPresent)
        {
            Logger.Verbose("Character is null but it shouldn't be, waiting");
            Thread.Sleep(50);
        }
        _dalamudUtil.WaitWhileCharacterIsDrawing(_dalamudUtil.PlayerPointer);
        var cache = new CharacterData
        {
            GlamourerString = _ipcManager.GlamourerGetCharacterCustomization(_dalamudUtil.PlayerCharacter),
            ManipulationString = _ipcManager.PenumbraGetMetaManipulations()
        };

        var human = (Human*)((Character*)_dalamudUtil.PlayerPointer)->GameObject.GetDrawObject();
        for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
        {
            var mdl = (RenderModel*)human->CharacterBase.ModelArray[mdlIdx];
            if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
            {
                continue;
            }

            AddReplacementsFromRenderModel(mdl, cache, 0, "Character");
        }

        var weaponObject = (Weapon*)((Object*)human)->ChildObject;

        if ((IntPtr)weaponObject != IntPtr.Zero)
        {
            var mainHandWeapon = weaponObject->WeaponRenderModel->RenderModel;

            AddReplacementsFromRenderModel(mainHandWeapon, cache, 0, "Weapon");

            if (weaponObject->NextSibling != (IntPtr)weaponObject)
            {
                var offHandWeapon = ((Weapon*)weaponObject->NextSibling)->WeaponRenderModel->RenderModel;

                AddReplacementsFromRenderModel(offHandWeapon, cache, 1, "OffHand Weapon");
            }
        }

        AddReplacementsFromTexture(new Utf8String(((HumanExt*)human)->Decal->FileName()).ToString(), cache, 0, "Decal", false);
        AddReplacementsFromTexture(new Utf8String(((HumanExt*)human)->LegacyBodyDecal->FileName()).ToString(), cache, 0, "Legacy Decal", false);

        st.Stop();
        Logger.Verbose("Building Character Data took " + st.Elapsed);

        return cache;
    }

    private FileReplacement CreateFileReplacement(string path, bool doNotReverseResolve = false)
    {
        var fileReplacement = new FileReplacement(_ipcManager.PenumbraModDirectory()!);
        if (!doNotReverseResolve)
        {
            fileReplacement.GamePaths =
                _ipcManager.PenumbraReverseResolvePlayer(path).ToList();
            fileReplacement.SetResolvedPath(path);
        }
        else
        {
            fileReplacement.GamePaths = new List<string> { path };
            fileReplacement.SetResolvedPath(_ipcManager.PenumbraResolvePath(path, _dalamudUtil.PlayerName)!);
        }

        return fileReplacement;
    }
}
