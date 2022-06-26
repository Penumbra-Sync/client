using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Colors;
using ImGuiNET;
using MareSynchronos.Managers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI
{
    public class UiShared
    {
        private readonly IpcManager _ipcManager;
        private readonly ApiController _apiController;
        private readonly FileCacheManager _fileCacheManager;
        private readonly Configuration _pluginConfiguration;
        public long FileCacheSize => _fileCacheManager.FileCacheSize;

        public UiShared(IpcManager ipcManager, ApiController apiController, FileCacheManager fileCacheManager, Configuration pluginConfiguration)
        {
            _ipcManager = ipcManager;
            _apiController = apiController;
            _fileCacheManager = fileCacheManager;
            _pluginConfiguration = pluginConfiguration;
        }

        public bool DrawOtherPluginState()
        {
            var penumbraExists = _ipcManager.CheckPenumbraApi();
            var glamourerExists = _ipcManager.CheckGlamourerApi();

            var penumbraColor = penumbraExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            var glamourerColor = glamourerExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            ImGui.Text("Penumbra:");
            ImGui.SameLine();
            ImGui.TextColored(penumbraColor, penumbraExists ? "Available" : "Unavailable");
            ImGui.SameLine();
            ImGui.Text("Glamourer:");
            ImGui.SameLine();
            ImGui.TextColored(glamourerColor, glamourerExists ? "Available" : "Unavailable");

            if (!penumbraExists || !glamourerExists)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed, "You need to install both Penumbra and Glamourer and keep them up to date to use Mare Synchronos.");
                return false;
            }

            return true;
        }

        public void DrawFileScanState()
        {
            ImGui.Text("File Scanner Status");
            if (_fileCacheManager.IsScanRunning)
            {
                ImGui.Text("Scan is running");
                ImGui.Text("Current Progress:");
                ImGui.SameLine();
                ImGui.Text(_fileCacheManager.TotalFiles <= 0
                    ? "Collecting files"
                    : $"Processing {_fileCacheManager.CurrentFileProgress} / {_fileCacheManager.TotalFiles} files");
            }
            else
            {
                ImGui.Text("Watching Penumbra Directory: " + _fileCacheManager.WatchedPenumbraDirectory);
                ImGui.Text("Watching Cache Directory: " + _fileCacheManager.WatchedCacheDirectory);
            }
        }

        public void PrintServerState()
        {
            ImGui.Text("Service status of " + (string.IsNullOrEmpty(_pluginConfiguration.ApiUri) ? ApiController.MainServer : _pluginConfiguration.ApiUri));
            ImGui.SameLine();
            var color = _apiController.ServerAlive ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            ImGui.TextColored(color, _apiController.ServerAlive ? "Available" : "Unavailable");
        }

        public static void TextWrapped(string text)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }

        public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

        public static Vector4 UploadColor((long, long) data) => data.Item1 == 0 ? ImGuiColors.DalamudGrey :
            data.Item1 == data.Item2 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudYellow;

        public static uint Color(byte r, byte g, byte b, byte a)
        { uint ret = a; ret <<= 8; ret += b; ret <<= 8; ret += g; ret <<= 8; ret += r; return ret; }

        public static void DrawOutlinedFont(ImDrawListPtr drawList, string text, Vector2 textPos, uint fontColor, uint outlineColor, int thickness)
        {
            drawList.AddText(textPos with { Y = textPos.Y - thickness },
                outlineColor, text);
            drawList.AddText(textPos with { X = textPos.X - thickness },
                outlineColor, text);
            drawList.AddText(textPos with { Y = textPos.Y + thickness },
                outlineColor, text);
            drawList.AddText(textPos with { X = textPos.X + thickness },
                outlineColor, text);
            drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y - thickness),
                outlineColor, text);
            drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y + thickness),
                outlineColor, text);
            drawList.AddText(new Vector2(textPos.X - thickness, textPos.Y + thickness),
                outlineColor, text);
            drawList.AddText(new Vector2(textPos.X + thickness, textPos.Y - thickness),
                outlineColor, text);

            drawList.AddText(textPos, fontColor, text);
            drawList.AddText(textPos, fontColor, text);
        }

        public static string ByteToString(long bytes)
        {
            string[] suffix = { "B", "KiB", "MiB", "GiB", "TiB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return $"{dblSByte:0.00} {suffix[i]}";
        }

        private int _serverSelectionIndex = 0;

        public void DrawServiceSelection()
        {
            string[] comboEntries = new[] { ApiController.MainServer, "Custom Service" };
            if (ImGui.BeginCombo("Service", comboEntries[_serverSelectionIndex]))
            {
                for (int n = 0; n < comboEntries.Length; n++)
                {
                    bool isSelected = _serverSelectionIndex == n;
                    if (ImGui.Selectable(comboEntries[n], isSelected))
                    {
                        _serverSelectionIndex = n;
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }

                    bool useCustomService = _serverSelectionIndex != 0;

                    if (_apiController.UseCustomService != useCustomService)
                    {
                        _apiController.UseCustomService = useCustomService;
                        _pluginConfiguration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            if (_apiController.UseCustomService)
            {
                string serviceAddress = _pluginConfiguration.ApiUri;
                if (ImGui.InputText("Service address", ref serviceAddress, 255))
                {
                    if (_pluginConfiguration.ApiUri != serviceAddress)
                    {
                        _pluginConfiguration.ApiUri = serviceAddress;
                        _apiController.RestartHeartbeat();
                        _pluginConfiguration.Save();
                    }
                }
            }

            PrintServerState();
            if (_apiController.ServerAlive)
            {
                if (ImGui.Button("Register"))
                {
                    Task.WaitAll(_apiController.Register());
                }
            }
        }

        public static void DrawHelpText(string helpText)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(helpText);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

        public void DrawCacheDirectorySetting()
        {
            var cacheDirectory = _pluginConfiguration.CacheFolder;
            if (ImGui.InputText("Cache Folder##cache", ref cacheDirectory, 255))
            {
                _pluginConfiguration.CacheFolder = cacheDirectory;
                if (!string.IsNullOrEmpty(_pluginConfiguration.CacheFolder) && Directory.Exists(_pluginConfiguration.CacheFolder))
                {
                    _pluginConfiguration.Save();
                    _fileCacheManager.StartWatchers();
                }
            }

            if (!Directory.Exists(cacheDirectory))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                TextWrapped("The folder you selected does not exist. Please provide a valid path.");
                ImGui.PopStyleColor();
            }
        }

        public void DrawParallelScansSetting()
        {
            var parallelScans = _pluginConfiguration.MaxParallelScan;
            if (ImGui.SliderInt("Parallel File Scans##parallelism", ref parallelScans, 1, 20))
            {
                _pluginConfiguration.MaxParallelScan = parallelScans;
                _pluginConfiguration.Save();
            }
        }
    }
}
