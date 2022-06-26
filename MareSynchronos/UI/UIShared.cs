using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
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
        private readonly FileDialogManager _fileDialogManager;
        private readonly Configuration _pluginConfiguration;
        public long FileCacheSize => _fileCacheManager.FileCacheSize;

        public UiShared(IpcManager ipcManager, ApiController apiController, FileCacheManager fileCacheManager, FileDialogManager fileDialogManager, Configuration pluginConfiguration)
        {
            _ipcManager = ipcManager;
            _apiController = apiController;
            _fileCacheManager = fileCacheManager;
            _fileDialogManager = fileDialogManager;
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
            var serverName = _apiController.ServerDictionary.ContainsKey(_pluginConfiguration.ApiUri)
                ? _apiController.ServerDictionary[_pluginConfiguration.ApiUri]
                : _pluginConfiguration.ApiUri;
            ImGui.Text("Service status of " + serverName);
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
        private string _customServerName = "";
        private string _customServerUri = "";

        public void DrawServiceSelection()
        {
            string[] comboEntries = _apiController.ServerDictionary.Values.ToArray();
            _serverSelectionIndex = Array.IndexOf(_apiController.ServerDictionary.Keys.ToArray(), _pluginConfiguration.ApiUri);
            if (ImGui.BeginCombo("Select Service", comboEntries[_serverSelectionIndex]))
            {
                for (int i = 0; i < comboEntries.Length; i++)
                {
                    bool isSelected = _serverSelectionIndex == i;
                    if (ImGui.Selectable(comboEntries[i], isSelected))
                    {
                        _pluginConfiguration.ApiUri = _apiController.ServerDictionary.Single(k => k.Value == comboEntries[i]).Key;
                        _pluginConfiguration.Save();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            if (_serverSelectionIndex != 0)
            {
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString() + "##deleteService"))
                {
                    _pluginConfiguration.CustomServerList.Remove(_pluginConfiguration.ApiUri);
                    _pluginConfiguration.ApiUri = ApiController.MainServiceUri;
                    _pluginConfiguration.Save();
                }
                ImGui.PopFont();
            }

            PrintServerState();

            if (_apiController.ServerAlive && !_pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri))
            {
                if (ImGui.Button("Register"))
                {
                    _pluginConfiguration.FullPause = false;
                    _pluginConfiguration.Save();
                    Task.WaitAll(_apiController.Register());
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                TextWrapped("You already have an account on this server.");
                ImGui.PopStyleColor();
                ImGui.SameLine();
                if (ImGui.Button("Connect##connectToService"))
                {
                    _pluginConfiguration.FullPause = false;
                    _pluginConfiguration.Save();
                    Task.Run(_apiController.CreateConnections);
                }
            }

            if (ImGui.TreeNode("Custom Service"))
            {
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("Custom Service Name", ref _customServerName, 255);
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("Custom Service Address", ref _customServerUri, 255);
                if (ImGui.Button("Add Custom Service"))
                {
                    if (!string.IsNullOrEmpty(_customServerUri) 
                        && !string.IsNullOrEmpty(_customServerName)
                        && !_pluginConfiguration.CustomServerList.ContainsValue(_customServerName))
                    {
                        _pluginConfiguration.CustomServerList[_customServerUri] = _customServerName;
                        _customServerUri = string.Empty;
                        _customServerName = string.Empty;
                        _pluginConfiguration.Save();
                    }
                }
                ImGui.TreePop();
            }
        }

        public static void DrawHelpText(string helpText)
        {
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SetWindowFontScale(0.8f);
            ImGui.TextDisabled(FontAwesomeIcon.Question.ToIconString());
            ImGui.SetWindowFontScale(1.0f);
            ImGui.PopFont();
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

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            string folderIcon = FontAwesomeIcon.Folder.ToIconString();
            if (ImGui.Button(folderIcon + "##chooseCacheFolder"))
            {
                _fileDialogManager.OpenFolderDialog("Pick Mare Synchronos Cache Folder", (success, path) =>
                {
                    if (!success) return;

                    _pluginConfiguration.CacheFolder = path;
                    _pluginConfiguration.Save();
                    _fileCacheManager.StartWatchers();
                });
            }
            ImGui.PopFont();

            if (!Directory.Exists(cacheDirectory) || !IsDirectoryWritable(cacheDirectory))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                TextWrapped("The folder you selected does not exist. Please provide a valid path.");
                ImGui.PopStyleColor();
            }
        }

        public bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
        {
            try
            {
                using (FileStream fs = File.Create(
                           Path.Combine(
                               dirPath,
                               Path.GetRandomFileName()
                           ),
                           1,
                           FileOptions.DeleteOnClose)
                      )
                { }
                return true;
            }
            catch
            {
                if (throwIfFails)
                    throw;
                else
                    return false;
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
