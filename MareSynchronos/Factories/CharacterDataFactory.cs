using System.Diagnostics;
using Dalamud.Utility;
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

    public CharacterData BuildCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
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
            Logger.Warn("Could not create data for " + playerRelatedObject.ObjectKind);
            Logger.Warn(ex.Message);
            Logger.Warn(ex.StackTrace ?? string.Empty);
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
            _pathsToForwardResolve.Clear();
            _pathsToReverseResolve.Clear();
            return CreateCharacterData(previousData, playerRelatedObject, token);
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("Cancelled creating Character data");
            throw;
        }
        catch (Exception e)
        {
            Logger.Debug("Failed to create " + playerRelatedObject.ObjectKind + " data");
            Logger.Debug(e.Message);
            Logger.Debug(e.StackTrace ?? string.Empty);
        }

        previousData.FileReplacements = previousFileReplacements;
        previousData.GlamourerString = previousGlamourerData;
        return previousData;
    }

    private unsafe void AddReplacementsFromRenderModel(RenderModel* mdl)
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

        AddResolvePath(mdlPath);

        for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
        {
            var mtrl = (Material*)mdl->Materials[mtrlIdx];
            if (mtrl == null) continue;

            AddReplacementsFromMaterial(mtrl);
        }
    }

    private unsafe void AddReplacementsFromMaterial(Material* mtrl)
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


        AddResolvePath(mtrlPath);

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

            AddReplacementsFromTexture(texPath);
        }

        try
        {
            var shpkPath = "shader/sm5/shpk/" + new ByteString(mtrlResourceHandle->ShpkString).ToString();
            Logger.Verbose("Checking File Replacement for Shader " + shpkPath);
            AddReplacementsFromShader(shpkPath);
        }
        catch
        {
            Logger.Verbose("Could not find shpk for Material " + fileName);
        }
    }

    private void AddReplacement(string varPath, bool doNotReverseResolve = false)
    {
        if (varPath.IsNullOrEmpty()) return;

        AddResolvePath(varPath, doNotReverseResolve);
    }

    private void AddReplacementsFromShader(string shpkPath)
    {
        if (string.IsNullOrEmpty(shpkPath)) return;

        AddResolvePath(shpkPath, doNotReverseResolve: true);
    }

    private void AddReplacementsFromTexture(string texPath, bool doNotReverseResolve = true)
    {
        if (string.IsNullOrEmpty(texPath)) return;

        AddResolvePath(texPath, doNotReverseResolve);

        if (texPath.Contains("/--", StringComparison.Ordinal)) return;

        AddResolvePath(texPath.Insert(texPath.LastIndexOf('/') + 1, "--"), doNotReverseResolve);
    }

    private unsafe CharacterData CreateCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        var objectKind = playerRelatedObject.ObjectKind;
        var charaPointer = playerRelatedObject.Address;

        if (!previousData.FileReplacements.ContainsKey(objectKind))
        {
            previousData.FileReplacements[objectKind] = new(FileReplacementComparer.Instance);
        }

        _dalamudUtil.WaitWhileCharacterIsDrawing(playerRelatedObject.ObjectKind.ToString(), playerRelatedObject.Address, 30000, ct: token);

        Stopwatch st = Stopwatch.StartNew();

        Logger.Debug("Handling unprocessed update for " + objectKind);

        if (previousData.FileReplacements.ContainsKey(objectKind))
        {
            previousData.FileReplacements[objectKind].Clear();
        }

        var chara = _dalamudUtil.CreateGameObject(charaPointer)!;
        while (!DalamudUtil.IsObjectPresent(chara))
        {
            Logger.Verbose("Character is null but it shouldn't be, waiting");
            Thread.Sleep(50);
        }

        var human = (Human*)((Character*)charaPointer)->GameObject.GetDrawObject();

        for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
        {
            var mdl = (RenderModel*)human->CharacterBase.ModelArray[mdlIdx];
            if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();

            AddReplacementsFromRenderModel(mdl);
        }

        if (objectKind == ObjectKind.Player)
        {
            AddPlayerSpecificReplacements(objectKind, charaPointer, human);
        }

        if (objectKind == ObjectKind.Pet)
        {
            foreach (var item in previousData.FileReplacements[objectKind])
            {
                _transientResourceManager.AddSemiTransientResource(objectKind, item.GamePaths.First());
            }

            previousData.FileReplacements[objectKind].Clear();
        }


        Dictionary<string, List<string>> resolvedPaths = GetFileReplacementsFromPaths();
        previousData.FileReplacements[objectKind] = new HashSet<FileReplacement>(resolvedPaths.Select(c => new FileReplacement(c.Value, c.Key, _fileCacheManager)));

        previousData.ManipulationString = _ipcManager.PenumbraGetMetaManipulations();
        previousData.GlamourerString[objectKind] = _ipcManager.GlamourerGetCharacterCustomization(charaPointer);
        previousData.HeelsOffset = _ipcManager.GetHeelsOffset();
        previousData.CustomizePlusScale = _ipcManager.GetCustomizePlusScale();
        previousData.PalettePlusPalette = _ipcManager.PalettePlusBuildPalette();

        Logger.Debug("== Static Replacements ==");
        foreach (var item in previousData.FileReplacements[objectKind])
        {
            Logger.Debug(item.ToString());
        }

        Logger.Debug("Handling transient update for " + objectKind);
        _transientResourceManager.ClearTransientPaths(charaPointer, previousData.FileReplacements[objectKind].SelectMany(c => c.GamePaths).ToList());

        _pathsToForwardResolve.Clear();
        _pathsToReverseResolve.Clear();

        ManageSemiTransientData(objectKind, charaPointer);

        var resolvedTransientPaths = GetFileReplacementsFromPaths();
        Logger.Debug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new FileReplacement(c.Value, c.Key, _fileCacheManager)))
        {
            Logger.Debug(replacement.ToString());
            previousData.FileReplacements[objectKind].Add(replacement);
        }

        _transientResourceManager.CleanSemiTransientResources(objectKind, previousData.FileReplacements[objectKind].ToList());



        st.Stop();
        Logger.Info("Building character data for " + objectKind + " took " + st.ElapsedMilliseconds + "ms");
        return previousData;
    }

    private Dictionary<string, List<string>> GetFileReplacementsFromPaths()
    {
        var forwardPaths = _pathsToForwardResolve.ToArray();
        var reversePaths = _pathsToReverseResolve.ToArray();
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);
        var result = _ipcManager.PenumbraResolvePaths(_pathsToForwardResolve.ToArray(), _pathsToReverseResolve.ToArray());
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

    private unsafe void ManageSemiTransientData(ObjectKind objectKind, IntPtr charaPointer)
    {
        _transientResourceManager.PersistTransientResources(charaPointer, objectKind);

        foreach (var item in _transientResourceManager.GetSemiTransientResources(objectKind))
        {
            AddResolvePath(item, true);
        }
    }

    private unsafe void AddPlayerSpecificReplacements(ObjectKind objectKind, IntPtr charaPointer, Human* human)
    {
        var weaponObject = (Weapon*)((Object*)human)->ChildObject;

        if ((IntPtr)weaponObject != IntPtr.Zero)
        {
            var mainHandWeapon = weaponObject->WeaponRenderModel->RenderModel;

            AddReplacementsFromRenderModel(mainHandWeapon);

            foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)weaponObject))
            {
                Logger.Verbose("Found transient weapon resource: " + item);
                AddReplacement(item, doNotReverseResolve: true);
            }

            if (weaponObject->NextSibling != (IntPtr)weaponObject)
            {
                var offHandWeapon = ((Weapon*)weaponObject->NextSibling)->WeaponRenderModel->RenderModel;

                AddReplacementsFromRenderModel(offHandWeapon);

                foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)offHandWeapon))
                {
                    Logger.Verbose("Found transient offhand weapon resource: " + item);
                    AddReplacement(item, doNotReverseResolve: true);
                }
            }
        }

        AddReplacementSkeleton(((HumanExt*)human)->Human.RaceSexId);
        try
        {
            AddReplacementsFromTexture(new ByteString(((HumanExt*)human)->Decal->FileName()).ToString(), doNotReverseResolve: false);
        }
        catch
        {
            Logger.Warn("Could not get Decal data");
        }
        try
        {
            AddReplacementsFromTexture(new ByteString(((HumanExt*)human)->LegacyBodyDecal->FileName()).ToString(), doNotReverseResolve: false);
        }
        catch
        {
            Logger.Warn("Could not get Legacy Body Decal Data");
        }
    }

    private void AddReplacementSkeleton(ushort raceSexId)
    {
        string raceSexIdString = raceSexId.ToString("0000");

        string skeletonPath = $"chara/human/c{raceSexIdString}/skeleton/base/b0001/skl_c{raceSexIdString}b0001.sklb";

        AddResolvePath(skeletonPath, doNotReverseResolve: true);
    }

    private void AddResolvePath(string path, bool doNotReverseResolve = false)
    {
        if (doNotReverseResolve) _pathsToForwardResolve.Add(path.ToLowerInvariant());
        else _pathsToReverseResolve.Add(path.ToLowerInvariant());
    }

    private readonly HashSet<string> _pathsToForwardResolve = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pathsToReverseResolve = new(StringComparer.Ordinal);
}
