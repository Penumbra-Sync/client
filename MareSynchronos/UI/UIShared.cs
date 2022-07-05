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
using MareSynchronos.Utils;
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
        private readonly DalamudUtil _dalamudUtil;
        public long FileCacheSize => _fileCacheManager.FileCacheSize;
        public bool ShowClientSecret = true;
        public string PlayerName => _dalamudUtil.PlayerName;
        public bool EditTrackerPosition { get; set; }

        public UiShared(IpcManager ipcManager, ApiController apiController, FileCacheManager fileCacheManager, FileDialogManager fileDialogManager, Configuration pluginConfiguration, DalamudUtil dalamudUtil)
        {
            _ipcManager = ipcManager;
            _apiController = apiController;
            _fileCacheManager = fileCacheManager;
            _fileDialogManager = fileDialogManager;
            _pluginConfiguration = pluginConfiguration;
            _dalamudUtil = dalamudUtil;
            _isDirectoryWritable = IsDirectoryWritable(_pluginConfiguration.CacheFolder);
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

        public void PrintServerState(bool isIntroUi = false)
        {
            var serverName = _apiController.ServerDictionary.ContainsKey(_pluginConfiguration.ApiUri)
                ? _apiController.ServerDictionary[_pluginConfiguration.ApiUri]
                : _pluginConfiguration.ApiUri;
            ImGui.TextUnformatted("Service " + serverName + ":");
            ImGui.SameLine();
            var color = _apiController.ServerAlive ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
            ImGui.TextColored(color, _apiController.ServerAlive ? "Available" : "Unavailable");
            if (_apiController.ServerAlive)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted("(");
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.ParsedGreen, _apiController.OnlineUsers.ToString());
                ImGui.SameLine();
                ImGui.Text("Users Online" + (!isIntroUi ? " (server-wide)" : string.Empty));
                ImGui.SameLine();
                ImGui.Text(")");
            }
        }

        public static void ColorText(string text, Vector4 color)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
        }

        public static void ColorTextWrapped(string text, Vector4 color)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            TextWrapped(text);
            ImGui.PopStyleColor();
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
        private bool _enterSecretKey = false;

        public void DrawServiceSelection(Action? callBackOnExit = null, bool isIntroUi = false)
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
                        _ = _apiController.CreateConnections();
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
                    _pluginConfiguration.ApiUri = _apiController.ServerDictionary.First().Key;
                    _pluginConfiguration.Save();
                }
                ImGui.PopFont();
            }

            if (ImGui.TreeNode("Add Custom Service"))
            {
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("Custom Service Name", ref _customServerName, 255);
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("Custom Service Address", ref _customServerUri, 255);
                if (ImGui.Button("Add Custom Service"))
                {
                    if (!string.IsNullOrEmpty(_customServerUri)
                        && !string.IsNullOrEmpty(_customServerName)
                        && !_pluginConfiguration.CustomServerList.ContainsValue(_customServerName)
                        && !_pluginConfiguration.CustomServerList.ContainsKey(_customServerUri))
                    {
                        _pluginConfiguration.CustomServerList[_customServerUri] = _customServerName;
                        _customServerUri = string.Empty;
                        _customServerName = string.Empty;
                        _pluginConfiguration.Save();
                    }
                }
                ImGui.TreePop();
            }

            PrintServerState(isIntroUi);

            if (_apiController.ServerAlive && !_pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri))
            {
                if (!_enterSecretKey)
                {
                    if (ImGui.Button("Register"))
                    {
                        _pluginConfiguration.FullPause = false;
                        _pluginConfiguration.Save();
                        Task.Run(_apiController.Register);
                        ShowClientSecret = true;
                        callBackOnExit?.Invoke();
                    }

                    ImGui.SameLine();
                }
            }
            else
            {
                ColorTextWrapped("You already have an account on this server.", ImGuiColors.DalamudYellow);
                ImGui.SameLine();
                if (ImGui.Button("Connect##connectToService"))
                {
                    _pluginConfiguration.FullPause = false;
                    _pluginConfiguration.Save();
                    Task.Run(_apiController.CreateConnections);
                }
            }

            string checkboxText = _pluginConfiguration.ClientSecret.ContainsKey(_pluginConfiguration.ApiUri)
                ? "I want to switch accounts"
                : "I already have an account";
            ImGui.Checkbox(checkboxText, ref _enterSecretKey);

            if (_enterSecretKey)
            {
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("Enter Secret Key", ref _secretKey, 255);
                ImGui.SameLine();
                if (ImGui.Button("Save"))
                {
                    _pluginConfiguration.ClientSecret[_pluginConfiguration.ApiUri] = _secretKey;
                    _pluginConfiguration.Save();
                    _secretKey = string.Empty;
                    Task.Run(_apiController.CreateConnections);
                    ShowClientSecret = false;
                    _enterSecretKey = false;
                    callBackOnExit?.Invoke();
                }
            }
        }

        private string _secretKey = "";

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
            ColorTextWrapped("Note: The cache folder should be somewhere close to root (i.e. C:\\MareCache) in a new empty folder. DO NOT point this to your game folder. DO NOT point this to your Penumbra folder.", ImGuiColors.DalamudYellow);
            var cacheDirectory = _pluginConfiguration.CacheFolder;
            if (ImGui.InputText("Cache Folder##cache", ref cacheDirectory, 255))
            {
                _isPenumbraDirectory = cacheDirectory.ToLower() == _ipcManager.PenumbraModDirectory()?.ToLower();
                _isDirectoryWritable = IsDirectoryWritable(cacheDirectory);

                if (!string.IsNullOrEmpty(cacheDirectory)
                    && Directory.Exists(cacheDirectory)
                    && _isDirectoryWritable
                    && !_isPenumbraDirectory)
                {
                    _pluginConfiguration.CacheFolder = cacheDirectory;
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

                    _isDirectoryWritable = IsDirectoryWritable(path);
                    _isPenumbraDirectory = path.ToLower() == _ipcManager.PenumbraModDirectory()?.ToLower();
                    if (_isDirectoryWritable && !_isPenumbraDirectory)
                    {
                        _pluginConfiguration.CacheFolder = path;
                        _pluginConfiguration.Save();
                        _fileCacheManager.StartWatchers();
                    }
                });
            }
            ImGui.PopFont();

            if (_isPenumbraDirectory)
            {
                ColorTextWrapped("Do not point the cache path directly to the Penumbra directory. If necessary, make a subfolder in it.", ImGuiColors.DalamudRed);
            }
            else if (!Directory.Exists(cacheDirectory) || !_isDirectoryWritable)
            {
                ColorTextWrapped("The folder you selected does not exist or cannot be written to. Please provide a valid path.", ImGuiColors.DalamudRed);
            }



            int maxCacheSize = _pluginConfiguration.MaxLocalCacheInGiB;
            if (ImGui.SliderInt("Maximum Cache Size in GB", ref maxCacheSize, 1, 50, "%d GB"))
            {
                _pluginConfiguration.MaxLocalCacheInGiB = maxCacheSize;
                _pluginConfiguration.Save();
            }
        }

        private bool _isDirectoryWritable = false;
        private bool _isPenumbraDirectory = false;

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
