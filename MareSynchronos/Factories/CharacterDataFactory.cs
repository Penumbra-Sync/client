using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Interop;
using MareSynchronos.Managers;
using MareSynchronos.Models;
using MareSynchronos.Utils;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using Penumbra.String;
using Weapon = MareSynchronos.Interop.Weapon;
using MareSynchronos.FileCache;

namespace MareSynchronos.Factories;

public class CharacterDataFactory
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly TransientResourceManager _transientResourceManager;
    private readonly FileCacheManager _fileCacheManager;

    public CharacterDataFactory(DalamudUtil dalamudUtil, IpcManager ipcManager, TransientResourceManager transientResourceManager, FileCacheManager fileReplacementFactory)
    {
        Logger.Verbose("Creating " + nameof(CharacterDataFactory));
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _transientResourceManager = transientResourceManager;
        _fileCacheManager = fileReplacementFactory;
    }

    private unsafe bool CheckForNullDrawObject(IntPtr playerPointer)
    {
        return ((Character*)playerPointer)->GameObject.DrawObject == null;
    }

    public async Task<CharacterData> BuildCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        if (!_ipcManager.Initialized)
        {
            throw new InvalidOperationException("Penumbra is not connected");
        }

        bool pointerIsZero = true;
        try
        {
            pointerIsZero = playerRelatedObject.Address == IntPtr.Zero;
            try
            {
                pointerIsZero = CheckForNullDrawObject(playerRelatedObject.Address);
            }
            catch
            {
                pointerIsZero = true;
                Logger.Debug("NullRef for " + playerRelatedObject.ObjectKind);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not create data for " + playerRelatedObject.ObjectKind, ex);
        }

        if (pointerIsZero)
        {
            Logger.Verbose("Pointer was zero for " + playerRelatedObject.ObjectKind);
            previousData.FileReplacements.Remove(playerRelatedObject.ObjectKind);
            previousData.GlamourerString.Remove(playerRelatedObject.ObjectKind);
            return previousData;
        }

        var previousFileReplacements = previousData.FileReplacements.ToDictionary(d => d.Key, d => d.Value);
        var previousGlamourerData = previousData.GlamourerString.ToDictionary(d => d.Key, d => d.Value);

        try
        {
            return await CreateCharacterData(previousData, playerRelatedObject, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Cancelled creating Character data");
            throw;
        }
        catch (Exception e)
        {
            Logger.Warn("Failed to create " + playerRelatedObject.ObjectKind + " data", e);
        }

        previousData.FileReplacements = previousFileReplacements;
        previousData.GlamourerString = previousGlamourerData;
        return previousData;
    }

    private async Task<CharacterData> CreateCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        var objectKind = playerRelatedObject.ObjectKind;
        var charaPointer = playerRelatedObject.Address;

        Logger.Debug("Building character data for " + objectKind);

        if (!previousData.FileReplacements.ContainsKey(objectKind))
        {
            previousData.FileReplacements[objectKind] = new(FileReplacementComparer.Instance);
        }
        else
        {
            previousData.FileReplacements[objectKind].Clear();
        }

        // wait until chara is not drawing and present so nothing spontaneously explodes
        _dalamudUtil.WaitWhileCharacterIsDrawing(playerRelatedObject.ObjectKind.ToString(), playerRelatedObject.Address, 30000, ct: token);
        var chara = _dalamudUtil.CreateGameObject(charaPointer)!;
        while (!DalamudUtil.IsObjectPresent(chara))
        {
            Logger.Verbose("Character is null but it shouldn't be, waiting");
            await Task.Delay(50).ConfigureAwait(false);
        }

        Stopwatch st = Stopwatch.StartNew();

        // gather up data from ipc
        previousData.ManipulationString = await _dalamudUtil.RunOnFrameworkThread(_ipcManager.PenumbraGetMetaManipulations).ConfigureAwait(false);
        previousData.GlamourerString[playerRelatedObject.ObjectKind] = await _dalamudUtil.RunOnFrameworkThread(() => _ipcManager.GlamourerGetCharacterCustomization(playerRelatedObject.Address))
            .ConfigureAwait(false);
        previousData.HeelsOffset = await _dalamudUtil.RunOnFrameworkThread(_ipcManager.GetHeelsOffset).ConfigureAwait(false);
        previousData.CustomizePlusScale = await _dalamudUtil.RunOnFrameworkThread(_ipcManager.GetCustomizePlusScale).ConfigureAwait(false);
        previousData.PalettePlusPalette = await _dalamudUtil.RunOnFrameworkThread(_ipcManager.PalettePlusBuildPalette).ConfigureAwait(false);

        // gather static replacements from render model
        var (forwardResolve, reverseResolve) = BuildDataFromModel(objectKind, charaPointer, token);
        Dictionary<string, List<string>> resolvedPaths = GetFileReplacementsFromPaths(forwardResolve, reverseResolve);
        previousData.FileReplacements[objectKind] = 
            new HashSet<FileReplacement>(resolvedPaths.Select(c => new FileReplacement(c.Value, c.Key, _fileCacheManager)), FileReplacementComparer.Instance)
            .Where(p => p.HasFileReplacement).ToHashSet();

        Logger.Debug("== Static Replacements ==");
        foreach (var replacement in previousData.FileReplacements[objectKind].Where(i => i.HasFileReplacement).OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            Logger.Debug(replacement.ToString());
        }

        // if it's pet then it's summoner, if it's summoner we actually want to keep all filereplacements alive at all times 
        // or we get into redraw city for every change and nothing works properly
        if (objectKind == ObjectKind.Pet)
        {
            foreach (var item in previousData.FileReplacements[objectKind].Where(i => i.HasFileReplacement).SelectMany(p => p.GamePaths))
            {
                _transientResourceManager.AddSemiTransientResource(objectKind, item);
            }
        }

        Logger.Debug("Handling transient update for " + objectKind);

        // remove all potentially gathered paths from the transient resource manager that are resolved through static resolving
        _transientResourceManager.ClearTransientPaths(charaPointer, previousData.FileReplacements[objectKind].SelectMany(c => c.GamePaths).ToList());

        // get all remaining paths and resolve them
        var transientPaths = ManageSemiTransientData(objectKind, charaPointer);
        var resolvedTransientPaths = GetFileReplacementsFromPaths(transientPaths, new HashSet<string>(StringComparer.Ordinal));

        Logger.Debug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new FileReplacement(c.Value, c.Key, _fileCacheManager)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            Logger.Debug(replacement.ToString());
            previousData.FileReplacements[objectKind].Add(replacement);
        }

        // clean up all semi transient resources that don't have any file replacement (aka null resolve)
        _transientResourceManager.CleanUpSemiTransientResources(objectKind, previousData.FileReplacements[objectKind].ToList());

        // make sure we only return data that actually has file replacements
        foreach (var item in previousData.FileReplacements)
        {
            previousData.FileReplacements[item.Key] = new HashSet<FileReplacement>(item.Value.Where(v => v.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), FileReplacementComparer.Instance);
        }

        st.Stop();
        Logger.Info("Building character data for " + objectKind + " took " + st.ElapsedMilliseconds + "ms");
        return previousData;
    }

    private unsafe (HashSet<string> forwardResolve, HashSet<string> reverseResolve) BuildDataFromModel(ObjectKind objectKind, nint charaPointer, CancellationToken token)
    {
        HashSet<string> forwardResolve = new(StringComparer.Ordinal);
        HashSet<string> reverseResolve = new(StringComparer.Ordinal);
        var human = (Human*)((Character*)charaPointer)->GameObject.GetDrawObject();
        for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
        {
            var mdl = (RenderModel*)human->CharacterBase.ModelArray[mdlIdx];
            if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();

            AddReplacementsFromRenderModel(mdl, forwardResolve, reverseResolve);
        }

        if (objectKind == ObjectKind.Player)
        {
            AddPlayerSpecificReplacements(objectKind, charaPointer, human, forwardResolve, reverseResolve);
        }

        return (forwardResolve, reverseResolve);
    }

    private unsafe void AddPlayerSpecificReplacements(ObjectKind objectKind, IntPtr charaPointer, Human* human, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        var weaponObject = (Weapon*)((Object*)human)->ChildObject;

        if ((IntPtr)weaponObject != IntPtr.Zero)
        {
            var mainHandWeapon = weaponObject->WeaponRenderModel->RenderModel;

            AddReplacementsFromRenderModel(mainHandWeapon, forwardResolve, reverseResolve);

            foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)weaponObject))
            {
                Logger.Verbose("Found transient weapon resource: " + item);
                forwardResolve.Add(item);
            }

            if (weaponObject->NextSibling != (IntPtr)weaponObject)
            {
                var offHandWeapon = ((Weapon*)weaponObject->NextSibling)->WeaponRenderModel->RenderModel;

                AddReplacementsFromRenderModel(offHandWeapon, forwardResolve, reverseResolve);

                foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)offHandWeapon))
                {
                    Logger.Verbose("Found transient offhand weapon resource: " + item);
                    forwardResolve.Add(item);
                }
            }
        }

        AddReplacementSkeleton(((HumanExt*)human)->Human.RaceSexId, forwardResolve);
        try
        {
            AddReplacementsFromTexture(new ByteString(((HumanExt*)human)->Decal->FileName()).ToString(), forwardResolve, reverseResolve, doNotReverseResolve: false);
        }
        catch
        {
            Logger.Warn("Could not get Decal data");
        }
        try
        {
            AddReplacementsFromTexture(new ByteString(((HumanExt*)human)->LegacyBodyDecal->FileName()).ToString(), forwardResolve, reverseResolve, doNotReverseResolve: false);
        }
        catch
        {
            Logger.Warn("Could not get Legacy Body Decal Data");
        }
    }


    private unsafe void AddReplacementsFromRenderModel(RenderModel* mdl, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
        {
            return;
        }

        string mdlPath;
        try
        {
            mdlPath = new ByteString(mdl->ResourceHandle->FileName()).ToString();
        }
        catch
        {
            Logger.Warn("Could not get model data");
            return;
        }
        Logger.Verbose("Checking File Replacement for Model " + mdlPath);

        reverseResolve.Add(mdlPath);

        for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
        {
            var mtrl = (Material*)mdl->Materials[mtrlIdx];
            if (mtrl == null) continue;

            AddReplacementsFromMaterial(mtrl, forwardResolve, reverseResolve);
        }
    }

    private unsafe void AddReplacementsFromMaterial(Material* mtrl, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        string fileName;
        try
        {
            fileName = new ByteString(mtrl->ResourceHandle->FileName()).ToString();

        }
        catch
        {
            Logger.Warn("Could not get material data");
            return;
        }

        Logger.Verbose("Checking File Replacement for Material " + fileName);
        var mtrlPath = fileName.Split("|")[2];

        reverseResolve.Add(mtrlPath);

        var mtrlResourceHandle = (MtrlResource*)mtrl->ResourceHandle;
        for (var resIdx = 0; resIdx < mtrlResourceHandle->NumTex; resIdx++)
        {
            string? texPath = null;
            try
            {
                texPath = new ByteString(mtrlResourceHandle->TexString(resIdx)).ToString();
            }
            catch
            {
                Logger.Warn("Could not get Texture data for Material " + fileName);
            }

            if (string.IsNullOrEmpty(texPath)) continue;

            Logger.Verbose("Checking File Replacement for Texture " + texPath);

            AddReplacementsFromTexture(texPath, forwardResolve, reverseResolve);
        }

        try
        {
            var shpkPath = "shader/sm5/shpk/" + new ByteString(mtrlResourceHandle->ShpkString).ToString();
            Logger.Verbose("Checking File Replacement for Shader " + shpkPath);
            forwardResolve.Add(shpkPath);
        }
        catch
        {
            Logger.Verbose("Could not find shpk for Material " + fileName);
        }
    }

    private void AddReplacementsFromTexture(string texPath, HashSet<string> forwardResolve, HashSet<string> reverseResolve, bool doNotReverseResolve = true)
    {
        if (string.IsNullOrEmpty(texPath)) return;

        if (doNotReverseResolve)
            forwardResolve.Add(texPath);
        else
            reverseResolve.Add(texPath);

        if (texPath.Contains("/--", StringComparison.Ordinal)) return;

        var dx11Path = texPath.Insert(texPath.LastIndexOf('/') + 1, "--");
        if (doNotReverseResolve)
            forwardResolve.Add(dx11Path);
        else
            reverseResolve.Add(dx11Path);
    }

    private void AddReplacementSkeleton(ushort raceSexId, HashSet<string> forwardResolve)
    {
        string raceSexIdString = raceSexId.ToString("0000");

        string skeletonPath = $"chara/human/c{raceSexIdString}/skeleton/base/b0001/skl_c{raceSexIdString}b0001.sklb";
        forwardResolve.Add(skeletonPath);
    }

    private HashSet<string> ManageSemiTransientData(ObjectKind objectKind, IntPtr charaPointer)
    {
        _transientResourceManager.PersistTransientResources(charaPointer, objectKind);

        HashSet<string> pathsToResolve = new(StringComparer.Ordinal);
        foreach (var path in _transientResourceManager.GetSemiTransientResources(objectKind).Where(path => !string.IsNullOrEmpty(path)))
        {
            pathsToResolve.Add(path);
        }

        return pathsToResolve;
    }

    private Dictionary<string, List<string>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        var forwardPaths = forwardResolve.ToArray();
        var reversePaths = reverseResolve.ToArray();
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);
        var result = _ipcManager.PenumbraResolvePaths(forwardPaths, reversePaths);
        for (int i = 0; i < forwardPaths.Length; i++)
        {
            var filePath = result.forward[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
            {
                list.Add(forwardPaths[i].ToLowerInvariant());
            }
            else
            {
                resolvedPaths[filePath] = new List<string> { forwardPaths[i].ToLowerInvariant() };
            }
        }

        for (int i = 0; i < reversePaths.Length; i++)
        {
            var filePath = reversePaths[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
            {
                list.AddRange(result.reverse[i].Select(c => c.ToLowerInvariant()));
            }
            else
            {
                resolvedPaths[filePath] = new List<string>(result.reverse[i].Select(c => c.ToLowerInvariant()).ToList());
            }
        }

        return resolvedPaths;
    }
}
