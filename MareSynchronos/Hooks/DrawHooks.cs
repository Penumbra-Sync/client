using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MareSynchronos.Factories;
using MareSynchronos.Models;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MareSynchronos.Hooks
{
    public unsafe class DrawHooks : IDisposable
    {
        public const int ResolveMdlIdx = 73;
        public const int ResolveMtrlIdx = 82;

        [Signature("E8 ?? ?? ?? ?? 48 85 C0 74 21 C7 40", DetourName = "CharacterBaseCreateDetour")]
        public Hook<CharacterBaseCreateDelegate>? CharacterBaseCreateHook;
        [Signature("E8 ?? ?? ?? ?? 40 F6 C7 01 74 3A 40 F6 C7 04 75 27 48 85 DB 74 2F 48 8B 05 ?? ?? ?? ?? 48 8B D3 48 8B 48 30",
            DetourName = "CharacterBaseDestructorDetour")]
        public Hook<CharacterBaseDestructorDelegate>? CharacterBaseDestructorHook;
        [Signature("48 8D 05 ?? ?? ?? ?? 48 89 03 48 8D 8B ?? ?? ?? ?? 44 89 83 ?? ?? ?? ?? 48 8B C1", ScanType = ScanType.StaticAddress)]
        public IntPtr* DrawObjectHumanVTable;
        [Signature("E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9 74 ?? 33 D2 E8 ?? ?? ?? ?? 84 C0")]
        public Hook<EnableDrawDelegate>? EnableDrawHook;
        [Signature("4C 8B DC 49 89 5B ?? 49 89 73 ?? 55 57 41 55", DetourName = "LoadMtrlTexDetour")]
        public Hook<LoadMtrlFilesDelegate>? LoadMtrlTexHook;

        public event EventHandler? PlayerLoadEvent;

        public Hook<GeneralResolveDelegate>? ResolveMdlPathHook;
        public Hook<MaterialResolveDetour>? ResolveMtrlPathHook;
        private readonly ClientState clientState;
        private readonly Dictionary<IntPtr, ushort> DrawObjectToObject = new();
        private readonly FileReplacementFactory factory;
        private readonly GameGui gameGui;
        private readonly ObjectTable objectTable;
        private readonly DalamudPluginInterface pluginInterface;
        private ConcurrentDictionary<string, FileReplacement> cachedResources = new();
        private GameObject* lastGameObject = null;
        private ConcurrentBag<FileReplacement> loadedMaterials = new();

        public DrawHooks(DalamudPluginInterface pluginInterface, ClientState clientState, ObjectTable objectTable, FileReplacementFactory factory, GameGui gameGui)
        {
            this.pluginInterface = pluginInterface;
            this.clientState = clientState;
            this.objectTable = objectTable;
            this.factory = factory;
            this.gameGui = gameGui;
            SignatureHelper.Initialise(this);
        }

        public delegate IntPtr CharacterBaseCreateDelegate(uint a, IntPtr b, IntPtr c, byte d);
        public delegate void CharacterBaseDestructorDelegate(IntPtr drawBase);
        public delegate void EnableDrawDelegate(IntPtr gameObject, IntPtr b, IntPtr c, IntPtr d);
        public delegate IntPtr GeneralResolveDelegate(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4);
        public delegate byte LoadMtrlFilesDelegate(IntPtr mtrlResourceHandle);
        public delegate IntPtr MaterialResolveDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5);
        public delegate void OnModelLoadCompleteDelegate(IntPtr drawObject);
        public void Dispose()
        {
            DisableHumanHooks();
            DisposeHumanHooks();
        }

        public CharacterCache BuildCharacterCache()
        {
            foreach (var resource in cachedResources)
            {
                resource.Value.IsInUse = false;
                resource.Value.ImcData = string.Empty;
                resource.Value.Associated.Clear();
            }

            PluginLog.Verbose("Invaldated resource cache");

            var cache = new CharacterCache();

            try
            {
                var model = (CharacterBase*)((Character*)clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();
                for (var idx = 0; idx < model->SlotCount; ++idx)
                {
                    var mdl = (RenderModel*)model->ModelArray[idx];
                    if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                    {
                        continue;
                    }

                    var mdlResource = factory.Create(new Utf8String(mdl->ResourceHandle->FileName()).ToString());
                    var cachedMdlResource = cachedResources.First(r => r.Value.IsReplacedByThis(mdlResource)).Value;

                    var imc = (ResourceHandle*)model->IMCArray[idx];
                    if (imc != null)
                    {
                        byte[] imcData = new byte[imc->Data->DataLength / sizeof(long)];
                        Marshal.Copy((IntPtr)imc->Data->DataPtr, imcData, 0, (int)imc->Data->DataLength / sizeof(long));
                        string imcDataStr = BitConverter.ToString(imcData).Replace("-", "");
                        cachedMdlResource.ImcData = imcDataStr;
                    }
                    cache.AddAssociatedResource(cachedMdlResource, null!, null!);

                    for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                    {
                        var mtrl = (Material*)mdl->Materials[mtrlIdx];
                        if (mtrl == null) continue;

                        var mtrlFileResource = factory.Create(new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2]);
                        var cachedMtrlResource = cachedResources.First(r => r.Value.IsReplacedByThis(mtrlFileResource)).Value;
                        cache.AddAssociatedResource(cachedMtrlResource, cachedMdlResource, null!);

                        var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;
                        for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                        {
                            var texPath = new Utf8String(mtrlResource->TexString(resIdx));

                            if (string.IsNullOrEmpty(texPath.ToString())) continue;

                            var texResource = factory.Create(texPath.ToString());
                            var cachedTexResource = cachedResources.First(r => r.Value.IsReplacedByThis(texResource)).Value;
                            cache.AddAssociatedResource(cachedTexResource, cachedMdlResource, cachedMtrlResource);
                        }
                    }
                }
            } catch (Exception ex)
            {
                PluginLog.Error(ex, ex.Message);
            }

            return cache;
        }

        public List<FileReplacement> PrintRequestedResources()
        {
            var cache = BuildCharacterCache();

            PluginLog.Verbose("--- CURRENTLY LOADED FILES ---");

            PluginLog.Verbose(cache.ToString());

            PluginLog.Verbose("--- LOOSE FILES ---");

            foreach (var resource in cachedResources.Where(r => !r.Value.IsInUse).OrderBy(a => a.Value.GamePath))
            {
                PluginLog.Verbose(resource.Value.ToString());
            }

            return cache.FileReplacements;
        }

        public void StartHooks()
        {
            cachedResources.Clear();
            SetupHumanHooks();
            EnableHumanHooks();
            PluginLog.Verbose("Hooks enabled");
        }

        public void StopHooks()
        {
            DisableHumanHooks();
            DisposeHumanHooks();
        }

        private void AddRequestedResource(FileReplacement replacement)
        {
            if (!cachedResources.Any(a => a.Value.IsReplacedByThis(replacement)) || cachedResources.Any(c => c.Key == replacement.GamePath))
            {
                cachedResources[replacement.GamePath] = replacement;
            }
        }

        private IntPtr CharacterBaseCreateDetour(uint a, IntPtr b, IntPtr c, byte d)
        {
            var ret = CharacterBaseCreateHook!.Original(a, b, c, d);
            if (lastGameObject != null)
            {
                DrawObjectToObject[ret] = (lastGameObject->ObjectIndex);
            }

            return ret;
        }

        private void CharacterBaseDestructorDetour(IntPtr drawBase)
        {
            if (DrawObjectToObject.TryGetValue(drawBase, out ushort idx))
            {
                var gameObj = GetGameObjectFromDrawObject(drawBase, idx);
                if (clientState.LocalPlayer != null && gameObj == (GameObject*)clientState.LocalPlayer!.Address)
                {
                    //PluginLog.Verbose("Clearing resources");
                    //cachedResources.Clear();
                    DrawObjectToObject.Clear();
                }
            }
            CharacterBaseDestructorHook!.Original.Invoke(drawBase);
        }

        private void DisableHumanHooks()
        {
            ResolveMdlPathHook?.Disable();
            ResolveMdlPathHook?.Disable();
            ResolveMtrlPathHook?.Disable();
            EnableDrawHook?.Disable();
            LoadMtrlTexHook?.Disable();
            CharacterBaseCreateHook?.Disable();
            CharacterBaseDestructorHook?.Disable();
        }

        private void DisposeHumanHooks()
        {
            ResolveMdlPathHook?.Dispose();
            ResolveMtrlPathHook?.Dispose();
            EnableDrawHook?.Dispose();
            LoadMtrlTexHook?.Dispose();
            CharacterBaseCreateHook?.Dispose();
            CharacterBaseDestructorHook?.Dispose();
        }

        private void EnableDrawDetour(IntPtr gameObject, IntPtr b, IntPtr c, IntPtr d)
        {
            var oldObject = lastGameObject;
            lastGameObject = (GameObject*)gameObject;
            EnableDrawHook!.Original.Invoke(gameObject, b, c, d);
            lastGameObject = oldObject;
        }

        private void EnableHumanHooks()
        {
            if (ResolveMdlPathHook?.IsEnabled ?? false) return;

            ResolveMdlPathHook?.Enable();
            ResolveMtrlPathHook?.Enable();
            EnableDrawHook?.Enable();
            LoadMtrlTexHook?.Enable();
            CharacterBaseCreateHook?.Enable();
            CharacterBaseDestructorHook?.Enable();
        }

        private string? GetCardName()
        {
            var uiModule = (UIModule*)gameGui.GetUIModule();
            var agentModule = uiModule->GetAgentModule();
            var agent = (byte*)agentModule->GetAgentByInternalID(393);
            if (agent == null)
            {
                return null;
            }

            var data = *(byte**)(agent + 0x28);
            if (data == null)
            {
                return null;
            }

            var block = data + 0x7A;
            return new Utf8String(block).ToString();
        }

        private GameObject* GetGameObjectFromDrawObject(IntPtr drawObject, int gameObjectIdx)
        {
            var tmp = objectTable[gameObjectIdx];
            GameObject* gameObject;
            if (tmp != null)
            {
                gameObject = (GameObject*)tmp.Address;
                if (gameObject->DrawObject == (DrawObject*)drawObject)
                {
                    return gameObject;
                }
            }

            DrawObjectToObject.Remove(drawObject);
            return null;
        }

        private string? GetGlamourName()
        {
            var addon = gameGui.GetAddonByName("MiragePrismMiragePlate", 1);
            return addon == IntPtr.Zero ? null : GetPlayerName();
        }

        private string? GetInspectName()
        {
            var addon = gameGui.GetAddonByName("CharacterInspect", 1);
            if (addon == IntPtr.Zero)
            {
                return null;
            }

            var ui = (AtkUnitBase*)addon;
            if (ui->UldManager.NodeListCount < 60)
            {
                return null;
            }

            var text = (AtkTextNode*)ui->UldManager.NodeList[59];
            if (text == null || !text->AtkResNode.IsVisible)
            {
                text = (AtkTextNode*)ui->UldManager.NodeList[60];
            }

            return text != null ? text->NodeText.ToString() : null;
        }

        private string GetPlayerName()
        {
            return clientState.LocalPlayer!.Name.ToString();
        }

        private void LoadMtrlHelper(IntPtr mtrlResourceHandle)
        {
            if (mtrlResourceHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var mtrl = (MtrlResource*)mtrlResourceHandle;
                var mtrlPath = Utf8String.FromSpanUnsafe(mtrl->Handle.FileNameSpan(), true, null, true);
                PluginLog.Verbose("Attempting to resolve: " + mtrlPath.ToString());
                var mtrlResource = factory.Create(mtrlPath.ToString());
                var existingMat = loadedMaterials.FirstOrDefault(m => m.IsReplacedByThis(mtrlResource));
                if (existingMat != null)
                {
                    PluginLog.Verbose("Resolving material: " + existingMat.GamePath);
                    for (int i = 0; i < mtrl->NumTex; i++)
                    {
                        var texPath = new Utf8String(mtrl->TexString(i));
                        PluginLog.Verbose("Resolving tex: " + texPath.ToString());

                        AddRequestedResource(factory.Create(texPath.ToString()));
                    }

                    loadedMaterials = new(loadedMaterials.Except(new[] { existingMat }));
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "error");
            }
        }

        private byte LoadMtrlTexDetour(IntPtr mtrlResourceHandle)
        {
            LoadMtrlHelper(mtrlResourceHandle);
            var ret = LoadMtrlTexHook!.Original(mtrlResourceHandle);
            return ret;
        }

        private IntPtr ResolveMdlDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint modelType)
        => ResolvePathDetour(drawObject, ResolveMdlPathHook!.Original(drawObject, path, unk3, modelType));

        private IntPtr ResolveMtrlDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5)
            => ResolvePathDetour(drawObject, ResolveMtrlPathHook!.Original(drawObject, path, unk3, unk4, unk5));

        private unsafe IntPtr ResolvePathDetour(IntPtr drawObject, IntPtr path)
        {
            if (path == IntPtr.Zero || clientState.LocalPlayer == null)
            {
                return path;
            }

            var gamepath = new Utf8String((byte*)path);

            var playerName = GetPlayerName();
            var gameDrawObject = (DrawObject*)drawObject;
            GameObject* gameObject = lastGameObject;

            if (DrawObjectToObject.TryGetValue(drawObject, out ushort idx))
            {
                gameObject = GetGameObjectFromDrawObject(drawObject, DrawObjectToObject[drawObject]);
            }

            if (gameObject != null && (gameObject->DrawObject == null || gameObject->DrawObject == gameDrawObject))
            {
                // 240, 241, 242 and 243 might need Penumbra config readout
                var actualName = gameObject->ObjectIndex switch
                {
                    240 => GetPlayerName(), // character window
                    241 => GetInspectName() ?? GetCardName() ?? GetGlamourName(), // inspect, character card, glamour plate editor.
                    242 => GetPlayerName(), // try-on
                    243 => GetPlayerName(), // dye preview
                    _ => null,
                } ?? new Utf8String(gameObject->Name).ToString();

                if (actualName != playerName)
                {
                    return path;
                }

                PluginLog.Verbose("Resolving resource: " + gamepath.ToString());
                PlayerLoadEvent?.Invoke((IntPtr)gameObject, new EventArgs());

                var resource = factory.Create(gamepath.ToString());

                if (gamepath.ToString().EndsWith("mtrl"))
                {
                    loadedMaterials.Add(resource);
                }

                AddRequestedResource(resource);
            }

            return path;
        }

        private void SetupHumanHooks()
        {
            if (ResolveMdlPathHook != null) return;

            ResolveMdlPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveMdlIdx], ResolveMdlDetour);
            ResolveMtrlPathHook = new Hook<MaterialResolveDetour>(DrawObjectHumanVTable[ResolveMtrlIdx], ResolveMtrlDetour);
        }
    }
}
