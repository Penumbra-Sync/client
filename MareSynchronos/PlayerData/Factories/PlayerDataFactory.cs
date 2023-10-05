using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Interop;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;
using MareSynchronos.FileCache;
using Microsoft.Extensions.Logging;
using System.Globalization;
using MareSynchronos.PlayerData.Data;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using CharacterData = MareSynchronos.PlayerData.Data.CharacterData;
using Weapon = MareSynchronos.Interop.Weapon;

namespace MareSynchronos.PlayerData.Factories;

public class PlayerDataFactory
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileCacheManager _fileCacheManager;
    private readonly IpcManager _ipcManager;
    private readonly ILogger<PlayerDataFactory> _logger;
    private readonly PerformanceCollectorService _performanceCollector;
    private readonly TransientResourceManager _transientResourceManager;

    private static readonly string[] AllowedExtensionsForGamePaths = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".scd", ".skp", ".shpk" };

    public PlayerDataFactory(ILogger<PlayerDataFactory> logger, DalamudUtilService dalamudUtil, IpcManager ipcManager,
        TransientResourceManager transientResourceManager, FileCacheManager fileReplacementFactory,
        PerformanceCollectorService performanceCollector)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _ipcManager = ipcManager;
        _transientResourceManager = transientResourceManager;
        _fileCacheManager = fileReplacementFactory;
        _performanceCollector = performanceCollector;

        _logger.LogTrace("Creating " + nameof(PlayerDataFactory));
    }

    public async Task BuildCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        if (!_ipcManager.Initialized)
        {
            throw new InvalidOperationException("Penumbra is not connected");
        }

        if (playerRelatedObject == null) return;

        bool pointerIsZero = true;
        try
        {
            pointerIsZero = playerRelatedObject.Address == IntPtr.Zero;
            try
            {
                pointerIsZero = await CheckForNullDrawObject(playerRelatedObject.Address).ConfigureAwait(false);
            }
            catch
            {
                pointerIsZero = true;
                _logger.LogDebug("NullRef for {object}", playerRelatedObject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create data for {object}", playerRelatedObject);
        }

        if (pointerIsZero)
        {
            _logger.LogTrace("Pointer was zero for {objectKind}", playerRelatedObject.ObjectKind);
            previousData.FileReplacements.Remove(playerRelatedObject.ObjectKind);
            previousData.GlamourerString.Remove(playerRelatedObject.ObjectKind);
            previousData.CustomizePlusScale.Remove(playerRelatedObject.ObjectKind);
            return;
        }

        var previousFileReplacements = previousData.FileReplacements.ToDictionary(d => d.Key, d => d.Value);
        var previousGlamourerData = previousData.GlamourerString.ToDictionary(d => d.Key, d => d.Value);
        var previousCustomize = previousData.CustomizePlusScale.ToDictionary(d => d.Key, d => d.Value);

        try
        {
            await _performanceCollector.LogPerformance(this, "CreateCharacterData>" + playerRelatedObject.ObjectKind, async () =>
            {
                await CreateCharacterData(previousData, playerRelatedObject, token).ConfigureAwait(false);
            }).ConfigureAwait(true);
            return;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cancelled creating Character data for {object}", playerRelatedObject);
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to create {object} data", playerRelatedObject);
        }

        previousData.FileReplacements = previousFileReplacements;
        previousData.GlamourerString = previousGlamourerData;
        previousData.CustomizePlusScale = previousCustomize;
    }

    private unsafe void AddPlayerSpecificReplacements(Human* human, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        var weaponObject = (Weapon*)((Object*)human)->ChildObject;

        if ((IntPtr)weaponObject != IntPtr.Zero)
        {
            var mainHandWeapon = weaponObject->WeaponRenderModel->RenderModel;

            AddReplacementsFromRenderModel((Model*)mainHandWeapon, forwardResolve, reverseResolve);

            foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)weaponObject))
            {
                _logger.LogTrace("Found transient weapon resource: {item}", item);
                forwardResolve.Add(item);
            }

            if (weaponObject->NextSibling != (IntPtr)weaponObject)
            {
                var offHandWeapon = ((Weapon*)weaponObject->NextSibling)->WeaponRenderModel->RenderModel;

                AddReplacementsFromRenderModel((Model*)offHandWeapon, forwardResolve, reverseResolve);

                foreach (var item in _transientResourceManager.GetTransientResources((IntPtr)offHandWeapon))
                {
                    _logger.LogTrace("Found transient offhand weapon resource: {item}", item);
                    forwardResolve.Add(item);
                }
            }
        }

        AddReplacementSkeleton((human)->RaceSexId, forwardResolve);
        try
        {
            AddReplacementsFromTexture((human)->Decal->ResourceHandle.FileName.ToString(), forwardResolve, reverseResolve, doNotReverseResolve: false);
        }
        catch
        {
            _logger.LogWarning("Could not get Decal data");
        }
        try
        {
            AddReplacementsFromTexture((human)->LegacyBodyDecal->ResourceHandle.FileName.ToString(), forwardResolve, reverseResolve, doNotReverseResolve: false);
        }
        catch
        {
            _logger.LogWarning("Could not get Legacy Body Decal Data");
        }
    }

    private unsafe void AddReplacementsFromMaterial(Material* mtrl, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        string fileName;
        try
        {
            fileName = mtrl->MaterialResourceHandle->ResourceHandle.FileName.ToString();
        }
        catch
        {
            _logger.LogWarning("Could not get material data");
            return;
        }

        _logger.LogTrace("Checking File Replacement for Material {file}", fileName);
        var mtrlPath = fileName.Split("|")[2];

        reverseResolve.Add(mtrlPath);

        var mtrlResourceHandle = mtrl->MaterialResourceHandle;
        for (var resIdx = 0; resIdx < mtrlResourceHandle->TextureCount; resIdx++)
        {
            string? texPath = null;
            try
            {
                texPath = mtrlResourceHandle->TexturePathString(resIdx);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not get Texture data for Material {file}", fileName);
            }

            if (string.IsNullOrEmpty(texPath)) continue;

            AddReplacementsFromTexture(texPath, forwardResolve, reverseResolve);
        }

        try
        {
            var shpkPath = "shader/sm5/shpk/" + mtrlResourceHandle->ShpkNameString;
            _logger.LogTrace("Checking File Replacement for Shader {path}", shpkPath);
            forwardResolve.Add(shpkPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not find shpk for Material {path}", fileName);
        }
    }

    private unsafe void AddReplacementsFromRenderModel(Model* mdl, HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        if (mdl == null || mdl->ModelResourceHandle == null || (ResourceCategory)mdl->ModelResourceHandle->ResourceHandle.Type.Value != ResourceCategory.Chara)
        {
            return;
        }

        string mdlPath;
        try
        {
            mdlPath = mdl->ModelResourceHandle->ResourceHandle.FileName.ToString();
        }
        catch
        {
            _logger.LogWarning("Could not get model data");
            return;
        }
        _logger.LogTrace("Checking File Replacement for Model {path}", mdlPath);

        reverseResolve.Add(mdlPath);

        for (var mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
        {
            var mtrl = mdl->Materials[mtrlIdx];
            if (mtrl == null) continue;

            AddReplacementsFromMaterial(mtrl, forwardResolve, reverseResolve);
        }
    }

    private void AddReplacementsFromTexture(string texPath, HashSet<string> forwardResolve, HashSet<string> reverseResolve, bool doNotReverseResolve = true)
    {
        if (string.IsNullOrEmpty(texPath)) return;

        _logger.LogTrace("Checking File Replacement for Texture {path}", texPath);

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
        string raceSexIdString = raceSexId.ToString("0000", CultureInfo.InvariantCulture);

        string skeletonPath = $"chara/human/c{raceSexIdString}/skeleton/base/b0001/skl_c{raceSexIdString}b0001.sklb";

        _logger.LogTrace("Checking skeleton {path}", skeletonPath);

        forwardResolve.Add(skeletonPath);
    }

    private unsafe (HashSet<string> forwardResolve, HashSet<string> reverseResolve) BuildDataFromModel(ObjectKind objectKind, nint charaPointer, CancellationToken token)
    {
        HashSet<string> forwardResolve = new(StringComparer.Ordinal);
        HashSet<string> reverseResolve = new(StringComparer.Ordinal);
        var human = (Human*)((Character*)charaPointer)->GameObject.GetDrawObject();
        for (var mdlIdx = 0; mdlIdx < human->CharacterBase.SlotCount; ++mdlIdx)
        {
            var mdl = human->CharacterBase.Models[mdlIdx];
            if (mdl == null || mdl->ModelResourceHandle == null || (ResourceCategory)mdl->ModelResourceHandle->ResourceHandle.Type.Value != ResourceCategory.Chara)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();

            AddReplacementsFromRenderModel(mdl, forwardResolve, reverseResolve);
        }

        if (objectKind == ObjectKind.Player)
        {
            AddPlayerSpecificReplacements(human, forwardResolve, reverseResolve);
        }

        return (forwardResolve, reverseResolve);
    }

    private async Task<bool> CheckForNullDrawObject(IntPtr playerPointer)
    {
        return await _dalamudUtil.RunOnFrameworkThread(() => CheckForNullDrawObjectUnsafe(playerPointer)).ConfigureAwait(false);
    }

    private unsafe bool CheckForNullDrawObjectUnsafe(IntPtr playerPointer)
    {
        return ((Character*)playerPointer)->GameObject.DrawObject == null;
    }

    private async Task<CharacterData> CreateCharacterData(CharacterData previousData, GameObjectHandler playerRelatedObject, CancellationToken token)
    {
        var objectKind = playerRelatedObject.ObjectKind;
        var charaPointer = playerRelatedObject.Address;

        _logger.LogDebug("Building character data for {obj}", playerRelatedObject);

        if (!previousData.FileReplacements.ContainsKey(objectKind))
        {
            previousData.FileReplacements[objectKind] = new(FileReplacementComparer.Instance);
        }
        else
        {
            previousData.FileReplacements[objectKind].Clear();
        }

        if (previousData.CustomizePlusScale.ContainsKey(objectKind))
        {
            previousData.CustomizePlusScale.Remove(objectKind);
        }

        // wait until chara is not drawing and present so nothing spontaneously explodes
        await _dalamudUtil.WaitWhileCharacterIsDrawing(_logger, playerRelatedObject, Guid.NewGuid(), 30000, ct: token).ConfigureAwait(false);
        int totalWaitTime = 10000;
        while (!await _dalamudUtil.IsObjectPresentAsync(await _dalamudUtil.CreateGameObjectAsync(charaPointer).ConfigureAwait(false)).ConfigureAwait(false) && totalWaitTime > 0)
        {
            _logger.LogTrace("Character is null but it shouldn't be, waiting");
            await Task.Delay(50, token).ConfigureAwait(false);
            totalWaitTime -= 50;
        }

        Stopwatch st = Stopwatch.StartNew();

        // penumbra call, it's currently broken
        // var data = (await _ipcManager.PenumbraGetCharacterData(_logger, playerRelatedObject).ConfigureAwait(false))![0];
        // if (data == null) throw new InvalidOperationException("Penumbra returned null data");

        // gather static replacements from render model
        var (forwardResolve, reverseResolve) = await _dalamudUtil.RunOnFrameworkThread(() => BuildDataFromModel(objectKind, charaPointer, token)).ConfigureAwait(false);
        Dictionary<string, List<string>> resolvedPaths = await GetFileReplacementsFromPaths(forwardResolve, reverseResolve).ConfigureAwait(false);
        previousData.FileReplacements[objectKind] =
                new HashSet<FileReplacement>(resolvedPaths.Select(c => new FileReplacement(c.Value.ToArray(), c.Key, _fileCacheManager)), FileReplacementComparer.Instance)
                .Where(p => p.HasFileReplacement).ToHashSet();
        previousData.FileReplacements[objectKind].RemoveWhere(c => c.GamePaths.Any(g => !AllowedExtensionsForGamePaths.Any(e => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));

        _logger.LogDebug("== Static Replacements ==");
        foreach (var replacement in previousData.FileReplacements[objectKind].Where(i => i.HasFileReplacement).OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("=> {repl}", replacement);
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

        _logger.LogDebug("Handling transient update for {obj}", playerRelatedObject);

        // remove all potentially gathered paths from the transient resource manager that are resolved through static resolving
        _transientResourceManager.ClearTransientPaths(charaPointer, previousData.FileReplacements[objectKind].SelectMany(c => c.GamePaths).ToList());

        // get all remaining paths and resolve them
        var transientPaths = ManageSemiTransientData(objectKind, charaPointer);
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(transientPaths, new HashSet<string>(StringComparer.Ordinal)).ConfigureAwait(false);

        _logger.LogDebug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new FileReplacement(c.Value.ToArray(), c.Key, _fileCacheManager)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            _logger.LogDebug("=> {repl}", replacement);
            previousData.FileReplacements[objectKind].Add(replacement);
        }

        // clean up all semi transient resources that don't have any file replacement (aka null resolve)
        _transientResourceManager.CleanUpSemiTransientResources(objectKind, previousData.FileReplacements[objectKind].ToList());

        // make sure we only return data that actually has file replacements
        foreach (var item in previousData.FileReplacements)
        {
            previousData.FileReplacements[item.Key] = new HashSet<FileReplacement>(item.Value.Where(v => v.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), FileReplacementComparer.Instance);
        }

        // gather up data from ipc
        previousData.ManipulationString = _ipcManager.PenumbraGetMetaManipulations();
        Task<string> getHeelsOffset = _ipcManager.GetHeelsOffsetAsync();
        Task<string> getGlamourerData = _ipcManager.GlamourerGetCharacterCustomizationAsync(playerRelatedObject.Address);
        Task<string?> getCustomizeData = _ipcManager.GetCustomizePlusScaleAsync(playerRelatedObject.Address);
        Task<string> getPalettePlusData = _ipcManager.PalettePlusBuildPaletteAsync();
        previousData.GlamourerString[playerRelatedObject.ObjectKind] = await getGlamourerData.ConfigureAwait(false);
        _logger.LogDebug("Glamourer is now: {data}", previousData.GlamourerString[playerRelatedObject.ObjectKind]);
        var customizeScale = await getCustomizeData.ConfigureAwait(false);
        if (!string.IsNullOrEmpty(customizeScale))
        {
            previousData.CustomizePlusScale[playerRelatedObject.ObjectKind] = customizeScale;
            _logger.LogDebug("Customize is now: {data}", previousData.CustomizePlusScale[playerRelatedObject.ObjectKind]);
        }
        previousData.PalettePlusPalette = await getPalettePlusData.ConfigureAwait(false);
        _logger.LogDebug("Palette is now: {data}", previousData.PalettePlusPalette);
        previousData.HonorificData = _ipcManager.HonorificGetTitle();
        _logger.LogDebug("Honorific is now: {data}", previousData.HonorificData);
        previousData.HeelsData = await getHeelsOffset.ConfigureAwait(false);
        _logger.LogDebug("Heels is now: {heels}", previousData.HeelsData);

        st.Stop();
        _logger.LogInformation("Building character data for {obj} took {time}ms", objectKind, TimeSpan.FromTicks(st.ElapsedTicks).TotalMilliseconds);

        return previousData;
    }

    private async Task<Dictionary<string, List<string>>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve, HashSet<string> reverseResolve)
    {
        var forwardPaths = forwardResolve.ToArray();
        var reversePaths = reverseResolve.ToArray();
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);
        var (forward, reverse) = await _ipcManager.PenumbraResolvePathsAsync(forwardPaths, reversePaths).ConfigureAwait(false);
        for (int i = 0; i < forwardPaths.Length; i++)
        {
            var filePath = forward[i].ToLowerInvariant();
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
                list.AddRange(reverse[i].Select(c => c.ToLowerInvariant()));
            }
            else
            {
                resolvedPaths[filePath] = new List<string>(reverse[i].Select(c => c.ToLowerInvariant()).ToList());
            }
        }

        return resolvedPaths;
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
}