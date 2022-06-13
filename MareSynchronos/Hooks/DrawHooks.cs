using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using MareSynchronos.Models;
using Penumbra.GameData.ByteString;
using Penumbra.Interop.Structs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Hooks
{
    public unsafe class DrawHooks : IDisposable
    {
        [Signature("48 8D 05 ?? ?? ?? ?? 48 89 03 48 8D 8B ?? ?? ?? ?? 44 89 83 ?? ?? ?? ?? 48 8B C1", ScanType = ScanType.StaticAddress)]
        public IntPtr* DrawObjectHumanVTable;

        // [Signature( "48 8D 1D ?? ?? ?? ?? 48 C7 41", ScanType = ScanType.StaticAddress )]
        // public IntPtr* DrawObjectVTable;
        // 
        // [Signature( "48 8D 05 ?? ?? ?? ?? 45 33 C0 48 89 03 BA", ScanType = ScanType.StaticAddress )]
        // public IntPtr* DrawObjectDemihumanVTable;
        // 
        // [Signature( "48 8D 05 ?? ?? ?? ?? 48 89 03 33 C0 48 89 83 ?? ?? ?? ?? 48 89 83 ?? ?? ?? ?? C7 83", ScanType = ScanType.StaticAddress )]
        // public IntPtr* DrawObjectMonsterVTable;
        // 
        // public const int ResolveRootIdx  = 71;

        public const int ResolveSklbIdx = 72;
        public const int ResolveMdlIdx = 73;
        public const int ResolveSkpIdx = 74;
        public const int ResolvePhybIdx = 75;
        public const int ResolvePapIdx = 76;
        public const int ResolveTmbIdx = 77;
        public const int ResolveMPapIdx = 79;
        public const int ResolveImcIdx = 81;
        public const int ResolveMtrlIdx = 82;
        public const int ResolveDecalIdx = 83;
        public const int ResolveVfxIdx = 84;
        public const int ResolveEidIdx = 85;
        private readonly DalamudPluginInterface pluginInterface;
        private readonly ClientState clientState;
        private readonly ObjectTable objectTable;
        private readonly FileReplacementFactory factory;

        public delegate IntPtr GeneralResolveDelegate(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4);
        public delegate IntPtr MPapResolveDelegate(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, uint unk5);
        public delegate IntPtr MaterialResolveDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5);
        public delegate IntPtr EidResolveDelegate(IntPtr drawObject, IntPtr path, IntPtr unk3);

        public delegate void OnModelLoadCompleteDelegate(IntPtr drawObject);

        public Hook<GeneralResolveDelegate>? ResolveDecalPathHook;
        public Hook<EidResolveDelegate>? ResolveEidPathHook;
        public Hook<GeneralResolveDelegate>? ResolveImcPathHook;
        public Hook<MPapResolveDelegate>? ResolveMPapPathHook;
        public Hook<GeneralResolveDelegate>? ResolveMdlPathHook;
        public Hook<MaterialResolveDetour>? ResolveMtrlPathHook;
        public Hook<MaterialResolveDetour>? ResolvePapPathHook;
        public Hook<GeneralResolveDelegate>? ResolvePhybPathHook;
        public Hook<GeneralResolveDelegate>? ResolveSklbPathHook;
        public Hook<GeneralResolveDelegate>? ResolveSkpPathHook;
        public Hook<EidResolveDelegate>? ResolveTmbPathHook;
        public Hook<MaterialResolveDetour>? ResolveVfxPathHook;

        public DrawHooks(DalamudPluginInterface pluginInterface, ClientState clientState, ObjectTable objectTable, FileReplacementFactory factory)
        {
            this.pluginInterface = pluginInterface;
            this.clientState = clientState;
            this.objectTable = objectTable;
            this.factory = factory;
            SignatureHelper.Initialise(this);
        }

        public void StartHooks()
        {
            allRequestedResources.Clear();
            SetupHumanHooks();
            EnableHumanHooks();
            PluginLog.Debug("Hooks enabled");
        }

        public void StopHooks()
        {
            DisableHumanHooks();
            DisposeHumanHooks();
        }

        private void SetupHumanHooks()
        {
            if (ResolveDecalPathHook != null) return;

            ResolveDecalPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveDecalIdx], ResolveDecalDetour);
            ResolveEidPathHook = new Hook<EidResolveDelegate>(DrawObjectHumanVTable[ResolveEidIdx], ResolveEidDetour);
            ResolveImcPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveImcIdx], ResolveImcDetour);
            ResolveMPapPathHook = new Hook<MPapResolveDelegate>(DrawObjectHumanVTable[ResolveMPapIdx], ResolveMPapDetour);
            ResolveMdlPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveMdlIdx], ResolveMdlDetour);
            ResolveMtrlPathHook = new Hook<MaterialResolveDetour>(DrawObjectHumanVTable[ResolveMtrlIdx], ResolveMtrlDetour);
            ResolvePapPathHook = new Hook<MaterialResolveDetour>(DrawObjectHumanVTable[ResolvePapIdx], ResolvePapDetour);
            ResolvePhybPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolvePhybIdx], ResolvePhybDetour);
            ResolveSklbPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveSklbIdx], ResolveSklbDetour);
            ResolveSkpPathHook = new Hook<GeneralResolveDelegate>(DrawObjectHumanVTable[ResolveSkpIdx], ResolveSkpDetour);
            ResolveTmbPathHook = new Hook<EidResolveDelegate>(DrawObjectHumanVTable[ResolveTmbIdx], ResolveTmbDetour);
            ResolveVfxPathHook = new Hook<MaterialResolveDetour>(DrawObjectHumanVTable[ResolveVfxIdx], ResolveVfxDetour);
        }

        private void EnableHumanHooks()
        {
            if (ResolveDecalPathHook?.IsEnabled ?? false) return;

            ResolveDecalPathHook?.Enable();
            //ResolveEidPathHook?.Enable();
            //ResolveImcPathHook?.Enable();
            //ResolveMPapPathHook?.Enable();
            ResolveMdlPathHook?.Enable();
            ResolveMtrlPathHook?.Enable();
            //ResolvePapPathHook?.Enable();
            //ResolvePhybPathHook?.Enable();
            //ResolveSklbPathHook?.Enable();
            //ResolveSkpPathHook?.Enable();
            //ResolveTmbPathHook?.Enable();
            //ResolveVfxPathHook?.Enable();
            EnableDrawHook?.Enable();
            LoadMtrlTexHook?.Enable();
        }

        private void DisableHumanHooks()
        {
            ResolveDecalPathHook?.Disable();
            //ResolveEidPathHook?.Disable();
            //ResolveImcPathHook?.Disable();
            //ResolveMPapPathHook?.Disable();
            ResolveMdlPathHook?.Disable();
            ResolveMtrlPathHook?.Disable();
            //ResolvePapPathHook?.Disable();
            //ResolvePhybPathHook?.Disable();
            //ResolveSklbPathHook?.Disable();
            //ResolveSkpPathHook?.Disable();
            //ResolveTmbPathHook?.Disable();
            //ResolveVfxPathHook?.Disable();
            EnableDrawHook?.Disable();
            LoadMtrlTexHook?.Disable();
        }

        private void DisposeHumanHooks()
        {
            ResolveDecalPathHook?.Dispose();
            //ResolveEidPathHook?.Dispose();
            //ResolveImcPathHook?.Dispose();
            //ResolveMPapPathHook?.Dispose();
            ResolveMdlPathHook?.Dispose();
            ResolveMtrlPathHook?.Dispose();
            //ResolvePapPathHook?.Dispose();
            //ResolvePhybPathHook?.Dispose();
            //ResolveSklbPathHook?.Dispose();
            //ResolveSkpPathHook?.Dispose();
            //ResolveTmbPathHook?.Dispose();
            //ResolveVfxPathHook?.Dispose();
            EnableDrawHook?.Dispose();
            LoadMtrlTexHook?.Dispose();
        }

        // Humans
        private IntPtr ResolveDecalDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4)
            => ResolvePathDetour(drawObject, ResolveDecalPathHook!.Original(drawObject, path, unk3, unk4));

        private IntPtr ResolveEidDetour(IntPtr drawObject, IntPtr path, IntPtr unk3)
            => ResolvePathDetour(drawObject, ResolveEidPathHook!.Original(drawObject, path, unk3));

        private IntPtr ResolveImcDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4)
            => ResolvePathDetour(drawObject, ResolveImcPathHook!.Original(drawObject, path, unk3, unk4));

        private IntPtr ResolveMPapDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, uint unk5)
            => ResolvePathDetour(drawObject, ResolveMPapPathHook!.Original(drawObject, path, unk3, unk4, unk5));

        private IntPtr ResolveMdlDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint modelType)
        {
            return ResolvePathDetour(drawObject, ResolveMdlPathHook!.Original(drawObject, path, unk3, modelType));
        }

        private IntPtr ResolveMtrlDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5)
            => ResolvePathDetour(drawObject, ResolveMtrlPathHook!.Original(drawObject, path, unk3, unk4, unk5));

        private IntPtr ResolvePapDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5)
        {
            return ResolvePathDetour(drawObject, ResolvePapPathHook!.Original(drawObject, path, unk3, unk4, unk5));
        }

        private IntPtr ResolvePhybDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4)
        {
            return ResolvePathDetour(drawObject, ResolvePhybPathHook!.Original(drawObject, path, unk3, unk4));
        }

        private IntPtr ResolveSklbDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4)
        {
            return ResolvePathDetour(drawObject, ResolveSklbPathHook!.Original(drawObject, path, unk3, unk4));
        }

        private IntPtr ResolveSkpDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4)
        {
            return ResolvePathDetour(drawObject, ResolveSkpPathHook!.Original(drawObject, path, unk3, unk4));
        }

        private IntPtr ResolveTmbDetour(IntPtr drawObject, IntPtr path, IntPtr unk3)
            => ResolvePathDetour(drawObject, ResolveTmbPathHook!.Original(drawObject, path, unk3));

        private IntPtr ResolveVfxDetour(IntPtr drawObject, IntPtr path, IntPtr unk3, uint unk4, ulong unk5)
            => ResolvePathDetour(drawObject, ResolveVfxPathHook!.Original(drawObject, path, unk3, unk4, unk5));

        public delegate void EnableDrawDelegate(IntPtr gameObject, IntPtr b, IntPtr c, IntPtr d);

        [Signature("E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9 74 ?? 33 D2 E8 ?? ?? ?? ?? 84 C0")]
        public Hook<EnableDrawDelegate>? EnableDrawHook;

        public GameObject* LastGameObject { get; private set; }

        private void EnableDrawDetour(IntPtr gameObject, IntPtr b, IntPtr c, IntPtr d)
        {
            //PluginLog.Debug("Draw start");
            var oldObject = LastGameObject;
            LastGameObject = (GameObject*)gameObject;
            EnableDrawHook!.Original.Invoke(gameObject, b, c, d);
            LastGameObject = oldObject;
            //PluginLog.Debug("Draw end");
        }

        public delegate byte LoadMtrlFilesDelegate(IntPtr mtrlResourceHandle);
        [Signature("4C 8B DC 49 89 5B ?? 49 89 73 ?? 55 57 41 55", DetourName = "LoadMtrlTexDetour")]
        public Hook<LoadMtrlFilesDelegate>? LoadMtrlTexHook;

        private byte LoadMtrlTexDetour(IntPtr mtrlResourceHandle)
        {
            LoadMtrlHelper(mtrlResourceHandle);
            var ret = LoadMtrlTexHook!.Original(mtrlResourceHandle);
            return ret;
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
                var mtrlResource = factory.Create(mtrlPath.ToString());
                var existingMat = loadedMaterials.FirstOrDefault(m => m.IsReplacedByThis(mtrlResource));
                if (existingMat != null)
                {
                    for (int i = 0; i < mtrl->NumTex; i++)
                    {
                        var texPath = new Utf8String(mtrl->TexString(i));
                        AddRequestedResource(factory.Create(texPath.ToString()));
                    }

                    loadedMaterials.Remove(existingMat);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "error");
            }
        }

        private unsafe IntPtr ResolvePathDetour(IntPtr drawObject, IntPtr path)
        {
            if (path == IntPtr.Zero)
            {
                return path;
            }

            var gamepath = new Utf8String((byte*)path);

            var playerName = clientState.LocalPlayer.Name.ToString();
            var gameDrawObject = (DrawObject*)drawObject;
            var playerDrawObject = ((Character*)clientState.LocalPlayer.Address)->GameObject.GetDrawObject();

            if (LastGameObject != null && (LastGameObject->DrawObject == null || LastGameObject->DrawObject == gameDrawObject))
            {
                var owner = new Utf8String(LastGameObject->Name).ToString();
                if (owner != playerName)
                {
                    return path;
                }

                AddRequestedResource(factory.Create(gamepath.ToString()));
            }
            else if (playerDrawObject == gameDrawObject)
            {
                var resource = factory.Create(gamepath.ToString());
                if (gamepath.ToString().EndsWith("mtrl"))
                {
                    loadedMaterials.Add(resource);
                }

                AddRequestedResource(resource);
            }

            return path;
        }

        List<FileReplacement> loadedMaterials = new();
        ConcurrentBag<FileReplacement> allRequestedResources = new();

        public List<FileReplacement> PrintRequestedResources()
        {
            foreach (var resource in allRequestedResources)
            {
                PluginLog.Debug(resource.ToString());
            }
            //PluginLog.Debug("---");

            var model = (CharacterBase*)((Character*)clientState.LocalPlayer!.Address)->GameObject.GetDrawObject();

            List<FileReplacement> fluctuatingResources = new();

            for (var i = 0; i < model->SlotCount; ++i)
            {
                var mdl = (RenderModel*)model->ModelArray[i];

                if (mdl == null || mdl->ResourceHandle == null || mdl->ResourceHandle->Category != ResourceCategory.Chara)
                {
                    continue;
                }

                var resource = factory.Create(new Utf8String(mdl->ResourceHandle->FileName()).ToString());

                PluginLog.Debug("Checking model: " + resource);

                var mdlResourceRepl = allRequestedResources.FirstOrDefault(r => r.IsReplacedByThis(resource));

                if (mdlResourceRepl != null)
                {
                    //PluginLog.Debug("Fluctuating resource detected: " + mdlResourceRepl);
                    //allRequestedResources.Remove(mdlResourceRepl);
                    fluctuatingResources.Add(mdlResourceRepl);
                }
                else
                {
                    //var resolvedPath = ResolvePath(mdlFile);
                    //if (resolvedPath != mdlFile)
                    //{
                    //fluctuatingResources[mdlFile] = resolvedPath;
                    //}
                }

                for (int mtrlIdx = 0; mtrlIdx < mdl->MaterialCount; mtrlIdx++)
                {
                    var mtrl = (Material*)mdl->Materials[mtrlIdx];

                    if (mtrl == null) continue;

                    var mtrlresource = factory.Create(new Utf8String(mtrl->ResourceHandle->FileName()).ToString().Split("|")[2]);

                    var mtrlResourceRepl = allRequestedResources.FirstOrDefault(r => r.IsReplacedByThis(mtrlresource));
                    if (mtrlResourceRepl != null)
                    {
                        mdlResourceRepl.AddAssociated(mtrlResourceRepl);
                        //PluginLog.Debug("Fluctuating resource detected: " + mtrlResourceRepl);
                        //allRequestedResources.Remove(mtrlResourceRepl);
                        //fluctuatingResources.Add(mtrlResourceRepl);
                    }
                    else
                    {
                        //var resolvedPath = ResolvePath(mtrlPath);
                        //if (resolvedPath != mtrlPath)
                        //{
                        //    fluctuatingResources[mtrlPath] = resolvedPath;
                        //}
                    }

                    var mtrlResource = (MtrlResource*)mtrl->ResourceHandle;

                    for (int resIdx = 0; resIdx < mtrlResource->NumTex; resIdx++)
                    {
                        var path = new Utf8String(mtrlResource->TexString(resIdx));
                        var gamePath = Utf8GamePath.FromString(path.ToString(), out var p, true) ? p : Utf8GamePath.Empty;

                        var texResource = factory.Create(path.ToString());

                        var texResourceRepl = allRequestedResources.FirstOrDefault(r => r.IsReplacedByThis(texResource));
                        if (texResourceRepl != null)
                        {
                            //PluginLog.Debug("Fluctuating resource detected: " + texResourceRepl);
                            //allRequestedResources.Remove(texResourceRepl);
                            //fluctuatingResources.Add(texResourceRepl);
                            mtrlResourceRepl.AddAssociated(texResourceRepl);
                            //fluctuatingResources[existingResource.Key] = existingResource.Value;
                        }
                        else
                        {
                            //var resolvedPath = ResolvePath(path.ToString());
                            //if (resolvedPath != path.ToString())
                            //{
                            //    fluctuatingResources[path.ToString()] = resolvedPath;
                            //}
                        }
                    }
                }
            }

            PluginLog.Debug("---");

            foreach (var resource in fluctuatingResources.OrderBy(a => a.GamePath))
            {
                PluginLog.Debug(Environment.NewLine + resource.ToString());
            }

            PluginLog.Debug("---");

            /*foreach (var resource in allRequestedResources.Where(r => r.HasFileReplacement && r.Associated.Count == 0).OrderBy(a => a.GamePath))
            {
                PluginLog.Debug(resource.ToString());
            }*/

            return fluctuatingResources;
        }

        private void AddRequestedResource(FileReplacement replacement)
        {
            if (allRequestedResources.Any(a => a.IsReplacedByThis(replacement)))
            {
                PluginLog.Debug("Already added: " + replacement);
                return;
            }

            PluginLog.Debug("Adding: " + replacement.GamePath);

            allRequestedResources.Add(replacement);
        }

        public void Dispose()
        {
            DisableHumanHooks();
            DisposeHumanHooks();
        }
    }
}
