using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using MareSynchronos.FileCache;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public const string TooltipSeparator = "--SEP--";
    public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize |
                                               ImGuiWindowFlags.NoScrollbar |
                                           ImGuiWindowFlags.NoScrollWithMouse;

    public readonly FileDialogManager FileDialogManager;
    private const string _notesEnd = "##MARE_SYNCHRONOS_USER_NOTES_END##";
    private const string _notesStart = "##MARE_SYNCHRONOS_USER_NOTES_START##";
    private readonly ApiController _apiController;
    private readonly CacheMonitor _cacheMonitor;
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly Dalamud.Localization _localization;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, object?> _selectedComboItems = new(StringComparer.Ordinal);
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ITextureProvider _textureProvider;
    private readonly TokenProvider _tokenProvider;
    private bool _brioExists = false;
    private bool _cacheDirectoryHasOtherFilesThanCache = false;
    private bool _cacheDirectoryIsValidPath = true;
    private bool _customizePlusExists = false;
    private string _customServerName = "";
    private string _customServerUri = "";
    private Task<Uri?>? _discordOAuthCheck;
    private Task<string?>? _discordOAuthGetCode;
    private CancellationTokenSource _discordOAuthGetCts = new();
    private Task<Dictionary<string, string>>? _discordOAuthUIDs;
    private bool _glamourerExists = false;
    private bool _heelsExists = false;
    private bool _honorificExists = false;
    private bool _isDirectoryWritable = false;
    private bool _isOneDrive = false;
    private bool _isPenumbraDirectory = false;
    private bool _moodlesExists = false;
    private Dictionary<string, DateTime> _oauthTokenExpiry = new();
    private bool _penumbraExists = false;
    private bool _petNamesExists = false;
    private int _serverSelectionIndex = -1;
    public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController,
        CacheMonitor cacheMonitor, FileDialogManager fileDialogManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, IDalamudPluginInterface pluginInterface,
        ITextureProvider textureProvider,
        Dalamud.Localization localization,
        ServerConfigurationManager serverManager, TokenProvider tokenProvider, MareMediator mediator) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _apiController = apiController;
        _cacheMonitor = cacheMonitor;
        FileDialogManager = fileDialogManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pluginInterface;
        _textureProvider = textureProvider;
        _localization = localization;
        _serverConfigurationManager = serverManager;
        _tokenProvider = tokenProvider;
        _localization.SetupWithLangCode("en");

        _isDirectoryWritable = IsDirectoryWritable(_configService.Current.CacheFolder);

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) =>
        {
            _penumbraExists = _ipcManager.Penumbra.APIAvailable;
            _glamourerExists = _ipcManager.Glamourer.APIAvailable;
            _customizePlusExists = _ipcManager.CustomizePlus.APIAvailable;
            _heelsExists = _ipcManager.Heels.APIAvailable;
            _honorificExists = _ipcManager.Honorific.APIAvailable;
            _moodlesExists = _ipcManager.Moodles.APIAvailable;
            _petNamesExists = _ipcManager.PetNames.APIAvailable;
            _brioExists = _ipcManager.Brio.APIAvailable;
        });

        UidFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, new()
            {
                SizePx = 35
            }));
        });
        GameFont = _pluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new(GameFontFamilyAndSize.Axis12));
        IconFont = _pluginInterface.UiBuilder.IconFontFixedWidthHandle;
    }

    public static string DoubleNewLine => Environment.NewLine + Environment.NewLine;
    public ApiController ApiController => _apiController;

    public bool EditTrackerPosition { get; set; }

    public IFontHandle GameFont { get; init; }
    public bool HasValidPenumbraModPath => !(_ipcManager.Penumbra.ModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.Penumbra.ModDirectory);

    public IFontHandle IconFont { get; init; }
    public bool IsInGpose => _dalamudUtil.IsInGpose;

    public Dictionary<uint, string> JobData => _dalamudUtil.JobData.Value;
    public string PlayerName => _dalamudUtil.GetPlayerName();

    public IFontHandle UidFont { get; init; }
    public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;
    public uint WorldId => _dalamudUtil.GetHomeWorldId();

    public static void AttachToolTip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            if (text.Contains(TooltipSeparator, StringComparison.Ordinal))
            {
                var splitText = text.Split(TooltipSeparator, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < splitText.Length; i++)
                {
                    ImGui.TextUnformatted(splitText[i]);
                    if (i != splitText.Length - 1) ImGui.Separator();
                }
            }
            else
            {
                ImGui.TextUnformatted(text);
            }
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public static string ByteToString(long bytes, bool addSuffix = true)
    {
        string[] suffix = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static void CenterNextWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    public static uint Color(byte r, byte g, byte b, byte a)
    { uint ret = a; ret <<= 8; ret += b; ret <<= 8; ret += g; ret <<= 8; ret += r; return ret; }

    public static uint Color(Vector4 color)
    {
        uint ret = (byte)(color.W * 255);
        ret <<= 8;
        ret += (byte)(color.Z * 255);
        ret <<= 8;
        ret += (byte)(color.Y * 255);
        ret <<= 8;
        ret += (byte)(color.X * 255);
        return ret;
    }

    public static void ColorText(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    public static void ColorTextWrapped(string text, Vector4 color, float wrapPos = 0)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        TextWrapped(text, wrapPos);
    }

    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

    public static void DrawGrouped(Action imguiDrawAction, float rounding = 5f, float? expectedWidth = null)
    {
        var cursorPos = ImGui.GetCursorPos();
        using (ImRaii.Group())
        {
            if (expectedWidth != null)
            {
                ImGui.Dummy(new(expectedWidth.Value, 0));
                ImGui.SetCursorPos(cursorPos);
            }

            imguiDrawAction.Invoke();
        }

        ImGui.GetWindowDrawList().AddRect(
            ImGui.GetItemRectMin() - ImGui.GetStyle().ItemInnerSpacing,
            ImGui.GetItemRectMax() + ImGui.GetStyle().ItemInnerSpacing,
            Color(ImGuiColors.DalamudGrey2), rounding);
    }

    public static void DrawGroupedCenteredColorText(string text, Vector4 color, float? maxWidth = null)
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var textWidth = ImGui.CalcTextSize(text, wrapWidth: availWidth).X;
        if (maxWidth != null && textWidth > maxWidth * ImGuiHelpers.GlobalScale) textWidth = maxWidth.Value * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth / 2f) - (textWidth / 2f));
        DrawGrouped(() =>
        {
            ColorTextWrapped(text, color, ImGui.GetCursorPosX() + textWidth);
        }, expectedWidth: maxWidth == null ? null : maxWidth * ImGuiHelpers.GlobalScale);
    }

    public static void DrawOutlinedFont(string text, Vector4 fontColor, Vector4 outlineColor, int thickness)
    {
        var original = ImGui.GetCursorPos();

        using (ImRaii.PushColor(ImGuiCol.Text, outlineColor))
        {
            ImGui.SetCursorPos(original with { Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X - thickness, Y = original.Y + thickness });
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original with { X = original.X + thickness, Y = original.Y - thickness });
            ImGui.TextUnformatted(text);
        }

        using (ImRaii.PushColor(ImGuiCol.Text, fontColor))
        {
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
            ImGui.SetCursorPos(original);
            ImGui.TextUnformatted(text);
        }
    }

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

    public static void DrawTree(string leafName, Action drawOnOpened, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        using var tree = ImRaii.TreeNode(leafName, flags);
        if (tree)
        {
            drawOnOpened();
        }
    }

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public static string GetNotes(List<Pair> pairs)
    {
        StringBuilder sb = new();
        sb.AppendLine(_notesStart);
        foreach (var entry in pairs)
        {
            var note = entry.GetNote();
            if (note.IsNullOrEmpty()) continue;

            sb.Append(entry.UserData.UID).Append(":\"").Append(entry.GetNote()).AppendLine("\"");
        }
        sb.AppendLine(_notesEnd);

        return sb.ToString();
    }

    public static float GetWindowContentRegionWidth()
    {
        return ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
    }

    public static bool IsDirectoryWritable(string dirPath, bool throwIfFails = false)
    {
        try
        {
            using FileStream fs = File.Create(
                       Path.Combine(
                           dirPath,
                           Path.GetRandomFileName()
                       ),
                       1,
                       FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            if (throwIfFails)
                throw;

            return false;
        }
    }

    public static void ScaledNextItemWidth(float width)
    {
        ImGui.SetNextItemWidth(width * ImGuiHelpers.GlobalScale);
    }

    public static void ScaledSameLine(float offset)
    {
        ImGui.SameLine(offset * ImGuiHelpers.GlobalScale);
    }

    public static void SetScaledWindowSize(float width, bool centerWindow = true)
    {
        var newLineHeight = ImGui.GetCursorPosY();
        ImGui.NewLine();
        newLineHeight = ImGui.GetCursorPosY() - newLineHeight;
        var y = ImGui.GetCursorPos().Y + ImGui.GetWindowContentRegionMin().Y - newLineHeight * 2 - ImGui.GetStyle().ItemSpacing.Y;

        SetScaledWindowSize(width, y, centerWindow, scaledHeight: true);
    }

    public static void SetScaledWindowSize(float width, float height, bool centerWindow = true, bool scaledHeight = false)
    {
        ImGui.SameLine();
        var x = width * ImGuiHelpers.GlobalScale;
        var y = scaledHeight ? height : height * ImGuiHelpers.GlobalScale;

        if (centerWindow)
        {
            CenterWindow(x, y);
        }

        ImGui.SetWindowSize(new Vector2(x, y));
    }

    public static bool ShiftPressed() => (GetKeyState(0xA1) & 0x8000) != 0 || (GetKeyState(0xA0) & 0x8000) != 0;

    public static void TextWrapped(string text, float wrapPos = 0)
    {
        ImGui.PushTextWrapPos(wrapPos);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static Vector4 UploadColor((long, long) data) => data.Item1 == 0 ? ImGuiColors.DalamudGrey :
        data.Item1 == data.Item2 ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudYellow;

    public bool ApplyNotesFromClipboard(string notes, bool overwrite)
    {
        var splitNotes = notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        var splitNotesStart = splitNotes.FirstOrDefault();
        var splitNotesEnd = splitNotes.LastOrDefault();
        if (!string.Equals(splitNotesStart, _notesStart, StringComparison.Ordinal) || !string.Equals(splitNotesEnd, _notesEnd, StringComparison.Ordinal))
        {
            return false;
        }

        splitNotes.RemoveAll(n => string.Equals(n, _notesStart, StringComparison.Ordinal) || string.Equals(n, _notesEnd, StringComparison.Ordinal));

        foreach (var note in splitNotes)
        {
            try
            {
                var splittedEntry = note.Split(":", 2, StringSplitOptions.RemoveEmptyEntries);
                var uid = splittedEntry[0];
                var comment = splittedEntry[1].Trim('"');
                if (_serverConfigurationManager.GetNoteForUid(uid) != null && !overwrite) continue;
                _serverConfigurationManager.SetNoteForUid(uid, comment);
            }
            catch
            {
                Logger.LogWarning("Could not parse {note}", note);
            }
        }

        _serverConfigurationManager.SaveNotes();

        return true;
    }

    public void BigText(string text, Vector4? color = null)
    {
        FontText(text, UidFont, color);
    }

    public void BooleanToColoredIcon(bool value, bool inline = true)
    {
        using var colorgreen = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, value);
        using var colorred = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed, !value);

        if (inline) ImGui.SameLine();

        if (value)
        {
            IconText(FontAwesomeIcon.Check);
        }
        else
        {
            IconText(FontAwesomeIcon.Times);
        }
    }

    public void DrawCacheDirectorySetting()
    {
        ColorTextWrapped("Note: The storage folder should be somewhere close to root (i.e. C:\\MareStorage) in a new empty folder. DO NOT point this to your game folder. DO NOT point this to your Penumbra folder.", ImGuiColors.DalamudYellow);
        var cacheDirectory = _configService.Current.CacheFolder;
        ImGui.InputText("Storage Folder##cache", ref cacheDirectory, 255, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        using (ImRaii.Disabled(_cacheMonitor.MareWatcher != null))
        {
            if (IconButton(FontAwesomeIcon.Folder))
            {
                FileDialogManager.OpenFolderDialog("Pick Mare Synchronos Storage Folder", (success, path) =>
                {
                    if (!success) return;

                    _isOneDrive = path.Contains("onedrive", StringComparison.OrdinalIgnoreCase);
                    _isPenumbraDirectory = string.Equals(path.ToLowerInvariant(), _ipcManager.Penumbra.ModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    _cacheDirectoryHasOtherFilesThanCache = false;
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Length != 40 && !string.Equals(fileName, "desktop", StringComparison.OrdinalIgnoreCase))
                        {
                            _cacheDirectoryHasOtherFilesThanCache = true;
                            Logger.LogWarning("Found illegal file in {path}: {file}", path, file);
                            break;
                        }
                    }
                    var dirs = Directory.GetDirectories(path);
                    if (dirs.Any())
                    {
                        _cacheDirectoryHasOtherFilesThanCache = true;
                        Logger.LogWarning("Found folders in {path} not belonging to Mare: {dirs}", path, string.Join(", ", dirs));
                    }

                    _isDirectoryWritable = IsDirectoryWritable(path);
                    _cacheDirectoryIsValidPath = PathRegex().IsMatch(path);

                    if (!string.IsNullOrEmpty(path)
                        && Directory.Exists(path)
                        && _isDirectoryWritable
                        && !_isPenumbraDirectory
                        && !_isOneDrive
                        && !_cacheDirectoryHasOtherFilesThanCache
                        && _cacheDirectoryIsValidPath)
                    {
                        _configService.Current.CacheFolder = path;
                        _configService.Save();
                        _cacheMonitor.StartMareWatcher(path);
                        _cacheMonitor.InvokeScan();
                    }
                }, _dalamudUtil.IsWine ? @"Z:\" : @"C:\");
            }
        }
        if (_cacheMonitor.MareWatcher != null)
        {
            AttachToolTip("Stop the Monitoring before changing the Storage folder. As long as monitoring is active, you cannot change the Storage folder location.");
        }

        if (_isPenumbraDirectory)
        {
            ColorTextWrapped("Do not point the storage path directly to the Penumbra directory. If necessary, make a subfolder in it.", ImGuiColors.DalamudRed);
        }
        else if (_isOneDrive)
        {
            ColorTextWrapped("Do not point the storage path to a folder in OneDrive. Do not use OneDrive folders for any Mod related functionality.", ImGuiColors.DalamudRed);
        }
        else if (!_isDirectoryWritable)
        {
            ColorTextWrapped("The folder you selected does not exist or cannot be written to. Please provide a valid path.", ImGuiColors.DalamudRed);
        }
        else if (_cacheDirectoryHasOtherFilesThanCache)
        {
            ColorTextWrapped("Your selected directory has files or directories inside that are not Mare related. Use an empty directory or a previous Mare storage directory only.", ImGuiColors.DalamudRed);
        }
        else if (!_cacheDirectoryIsValidPath)
        {
            ColorTextWrapped("Your selected directory contains illegal characters unreadable by FFXIV. " +
                             "Restrict yourself to latin letters (A-Z), underscores (_), dashes (-) and arabic numbers (0-9).", ImGuiColors.DalamudRed);
        }

        float maxCacheSize = (float)_configService.Current.MaxLocalCacheInGiB;
        if (ImGui.SliderFloat("Maximum Storage Size in GiB", ref maxCacheSize, 1f, 200f, "%.2f GiB"))
        {
            _configService.Current.MaxLocalCacheInGiB = maxCacheSize;
            _configService.Save();
        }
        DrawHelpText("The storage is automatically governed by Mare. It will clear itself automatically once it reaches the set capacity by removing the oldest unused files. You typically do not need to clear it yourself.");
    }

    public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T?, string> toName,
        Action<T?>? onSelected = null, T? initialSelectedItem = default)
    {
        if (!comboItems.Any()) return default;

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            selectedItem = initialSelectedItem;
            _selectedComboItems[comboName] = selectedItem;
        }

        if (ImGui.BeginCombo(comboName, selectedItem == null ? "Unset Value" : toName((T?)selectedItem)))
        {
            foreach (var item in comboItems)
            {
                bool isSelected = EqualityComparer<T>.Default.Equals(item, (T?)selectedItem);
                if (ImGui.Selectable(toName(item), isSelected))
                {
                    _selectedComboItems[comboName] = item!;
                    onSelected?.Invoke(item!);
                }
            }

            ImGui.EndCombo();
        }

        return (T?)_selectedComboItems[comboName];
    }

    public void DrawFileScanState()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("File Scanner Status");
        ImGui.SameLine();
        if (_cacheMonitor.IsScanRunning)
        {
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("Scan is running");
            ImGui.TextUnformatted("Current Progress:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_cacheMonitor.TotalFiles == 1
                ? "Collecting files"
                : $"Processing {_cacheMonitor.CurrentFileProgress}/{_cacheMonitor.TotalFilesStorage} from storage ({_cacheMonitor.TotalFiles} scanned in)");
            AttachToolTip("Note: it is possible to have more files in storage than scanned in, " +
                "this is due to the scanner normally ignoring those files but the game loading them in and using them on your character, so they get " +
                "added to the local storage.");
        }
        else if (_cacheMonitor.HaltScanLocks.Any(f => f.Value > 0))
        {
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("Halted (" + string.Join(", ", _cacheMonitor.HaltScanLocks.Where(f => f.Value > 0).Select(locker => locker.Key + ": " + locker.Value + " halt requests")) + ")");
            ImGui.SameLine();
            if (ImGui.Button("Reset halt requests##clearlocks"))
            {
                _cacheMonitor.ResetLocks();
            }
        }
        else
        {
            ImGui.TextUnformatted("Idle");
            if (_configService.Current.InitialScanComplete)
            {
                ImGui.SameLine();
                if (IconTextButton(FontAwesomeIcon.Play, "Force rescan"))
                {
                    _cacheMonitor.InvokeScan();
                }
            }
        }
    }

    public void DrawHelpText(string helpText)
    {
        ImGui.SameLine();
        IconText(FontAwesomeIcon.QuestionCircle, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        AttachToolTip(helpText);
    }

    public void DrawOAuth(ServerStorage selectedServer)
    {
        var oauthToken = selectedServer.OAuthToken;
        _ = ImRaii.PushIndent(10f);
        if (oauthToken == null)
        {
            if (_discordOAuthCheck == null)
            {
                if (IconTextButton(FontAwesomeIcon.QuestionCircle, "Check if Server supports Discord OAuth2"))
                {
                    _discordOAuthCheck = _serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri);
                }
            }
            else
            {
                if (!_discordOAuthCheck.IsCompleted)
                {
                    ColorTextWrapped($"Checking OAuth2 compatibility with {selectedServer.ServerUri}", ImGuiColors.DalamudYellow);
                }
                else
                {
                    if (_discordOAuthCheck.Result != null)
                    {
                        ColorTextWrapped("Server is compatible with Discord OAuth2", ImGuiColors.HealerGreen);
                    }
                    else
                    {
                        ColorTextWrapped("Server is not compatible with Discord OAuth2", ImGuiColors.DalamudRed);
                    }
                }
            }

            if (_discordOAuthCheck != null && _discordOAuthCheck.IsCompleted)
            {
                if (IconTextButton(FontAwesomeIcon.ArrowRight, "Authenticate with Server"))
                {
                    _discordOAuthGetCode = _serverConfigurationManager.GetDiscordOAuthToken(_discordOAuthCheck.Result!, selectedServer.ServerUri, _discordOAuthGetCts.Token);
                }
                else if (_discordOAuthGetCode != null && !_discordOAuthGetCode.IsCompleted)
                {
                    TextWrapped("A browser window has been opened, follow it to authenticate. Click the button below if you accidentally closed the window and need to restart the authentication.");
                    if (IconTextButton(FontAwesomeIcon.Ban, "Cancel Authentication"))
                    {
                        _discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
                        _discordOAuthGetCode = null;
                    }
                }
                else if (_discordOAuthGetCode != null && _discordOAuthGetCode.IsCompleted)
                {
                    TextWrapped("Discord OAuth is completed, status: ");
                    ImGui.SameLine();
                    if (_discordOAuthGetCode.Result != null)
                    {
                        selectedServer.OAuthToken = _discordOAuthGetCode.Result;
                        _discordOAuthGetCode = null;
                        _serverConfigurationManager.Save();
                        ColorTextWrapped("Success", ImGuiColors.HealerGreen);
                    }
                    else
                    {
                        ColorTextWrapped("Failed, please check /xllog for more information", ImGuiColors.DalamudRed);
                    }
                }
            }
        }

        if (oauthToken != null)
        {
            if (!_oauthTokenExpiry.TryGetValue(oauthToken, out DateTime tokenExpiry))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(oauthToken);
                    tokenExpiry = _oauthTokenExpiry[oauthToken] = jwt.ValidTo;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not parse OAuth token, deleting");
                    selectedServer.OAuthToken = null;
                    _serverConfigurationManager.Save();
                }
            }

            if (tokenExpiry > DateTime.UtcNow)
            {
                ColorTextWrapped($"OAuth2 is enabled, linked to: Discord User {_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)}", ImGuiColors.HealerGreen);
                TextWrapped($"The OAuth2 token will expire on {tokenExpiry:yyyy-MM-dd} and automatically renew itself during login on or after {(tokenExpiry - TimeSpan.FromDays(7)):yyyy-MM-dd}.");
                using (ImRaii.Disabled(!CtrlPressed()))
                {
                    if (IconTextButton(FontAwesomeIcon.Exclamation, "Renew OAuth2 token manually") && CtrlPressed())
                    {
                        _ = _tokenProvider.TryUpdateOAuth2LoginTokenAsync(selectedServer, forced: true)
                            .ContinueWith((_) => _apiController.CreateConnectionsAsync());
                    }
                }
                DrawHelpText("Hold CTRL to manually refresh your OAuth2 token. Normally you do not need to do this.");
                ImGuiHelpers.ScaledDummy(10f);

                if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted)
                    && IconTextButton(FontAwesomeIcon.Question, "Check Discord Connection"))
                {
                    _discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, oauthToken);
                }
                else if (_discordOAuthUIDs != null)
                {
                    if (!_discordOAuthUIDs.IsCompleted)
                    {
                        ColorTextWrapped("Checking UIDs on Server", ImGuiColors.DalamudYellow);
                    }
                    else
                    {
                        var foundUids = _discordOAuthUIDs.Result?.Count ?? 0;
                        var primaryUid = _discordOAuthUIDs.Result?.FirstOrDefault() ?? new KeyValuePair<string, string>(string.Empty, string.Empty);
                        var vanity = string.IsNullOrEmpty(primaryUid.Value) ? "-" : primaryUid.Value;
                        if (foundUids > 0)
                        {
                            ColorTextWrapped($"Found {foundUids} associated UIDs on the server, Primary UID: {primaryUid.Key} (Vanity UID: {vanity})",
                                ImGuiColors.HealerGreen);
                        }
                        else
                        {
                            ColorTextWrapped($"Found no UIDs associated to this linked OAuth2 account", ImGuiColors.DalamudRed);
                        }
                    }
                }
            }
            else
            {
                ColorTextWrapped("The OAuth2 token is stale and expired. Please renew the OAuth2 connection.", ImGuiColors.DalamudRed);
                if (IconTextButton(FontAwesomeIcon.Exclamation, "Renew OAuth2 connection"))
                {
                    selectedServer.OAuthToken = null;
                    _serverConfigurationManager.Save();
                    _ = _serverConfigurationManager.CheckDiscordOAuth(selectedServer.ServerUri)
                        .ContinueWith(async (urlTask) =>
                        {
                            var url = await urlTask.ConfigureAwait(false);
                            var token = await _serverConfigurationManager.GetDiscordOAuthToken(url!, selectedServer.ServerUri, CancellationToken.None).ConfigureAwait(false);
                            selectedServer.OAuthToken = token;
                            _serverConfigurationManager.Save();
                            await _apiController.CreateConnectionsAsync().ConfigureAwait(false);
                        });
                }
            }

            DrawUnlinkOAuthButton(selectedServer);
        }
    }

    public bool DrawOtherPluginState()
    {
        ImGui.TextUnformatted("Mandatory Plugins:");

        ImGui.SameLine(150);
        ColorText("Penumbra", GetBoolColor(_penumbraExists));
        AttachToolTip($"Penumbra is " + (_penumbraExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("Glamourer", GetBoolColor(_glamourerExists));
        AttachToolTip($"Glamourer is " + (_glamourerExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.TextUnformatted("Optional Plugins:");
        ImGui.SameLine(150);
        ColorText("SimpleHeels", GetBoolColor(_heelsExists));
        AttachToolTip($"SimpleHeels is " + (_heelsExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("Customize+", GetBoolColor(_customizePlusExists));
        AttachToolTip($"Customize+ is " + (_customizePlusExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("Honorific", GetBoolColor(_honorificExists));
        AttachToolTip($"Honorific is " + (_honorificExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("Moodles", GetBoolColor(_moodlesExists));
        AttachToolTip($"Moodles is " + (_moodlesExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("PetNicknames", GetBoolColor(_petNamesExists));
        AttachToolTip($"PetNicknames is " + (_petNamesExists ? "available and up to date." : "unavailable or not up to date."));

        ImGui.SameLine();
        ColorText("Brio", GetBoolColor(_brioExists));
        AttachToolTip($"Brio is " + (_brioExists ? "available and up to date." : "unavailable or not up to date."));

        if (!_penumbraExists || !_glamourerExists)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "You need to install both Penumbra and Glamourer and keep them up to date to use Mare Synchronos.");
            return false;
        }

        return true;
    }

    public int DrawServiceSelection(bool selectOnChange = false, bool showConnect = true)
    {
        string[] comboEntries = _serverConfigurationManager.GetServerNames();

        if (_serverSelectionIndex == -1)
        {
            _serverSelectionIndex = Array.IndexOf(_serverConfigurationManager.GetServerApiUrls(), _serverConfigurationManager.CurrentApiUrl);
        }
        if (_serverSelectionIndex == -1 || _serverSelectionIndex >= comboEntries.Length)
        {
            _serverSelectionIndex = 0;
        }
        for (int i = 0; i < comboEntries.Length; i++)
        {
            if (string.Equals(_serverConfigurationManager.CurrentServer?.ServerName, comboEntries[i], StringComparison.OrdinalIgnoreCase))
                comboEntries[i] += " [Current]";
        }
        if (ImGui.BeginCombo("Select Service", comboEntries[_serverSelectionIndex]))
        {
            for (int i = 0; i < comboEntries.Length; i++)
            {
                bool isSelected = _serverSelectionIndex == i;
                if (ImGui.Selectable(comboEntries[i], isSelected))
                {
                    _serverSelectionIndex = i;
                    if (selectOnChange)
                    {
                        _serverConfigurationManager.SelectServer(i);
                    }
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (showConnect)
        {
            ImGui.SameLine();
            var text = "Connect";
            if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex) text = "Reconnect";
            if (IconTextButton(FontAwesomeIcon.Link, text))
            {
                _serverConfigurationManager.SelectServer(_serverSelectionIndex);
                _ = _apiController.CreateConnectionsAsync();
            }
        }

        if (ImGui.TreeNode("Add Custom Service"))
        {
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("Custom Service URI", ref _customServerUri, 255);
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("Custom Service Name", ref _customServerName, 255);
            if (IconTextButton(FontAwesomeIcon.Plus, "Add Custom Service")
                && !string.IsNullOrEmpty(_customServerUri)
                && !string.IsNullOrEmpty(_customServerName))
            {
                _serverConfigurationManager.AddServer(new ServerStorage()
                {
                    ServerName = _customServerName,
                    ServerUri = _customServerUri,
                    UseOAuth2 = true
                });
                _customServerName = string.Empty;
                _customServerUri = string.Empty;
                _configService.Save();
            }
            ImGui.TreePop();
        }

        return _serverSelectionIndex;
    }

    public void DrawUIDComboForAuthentication(int indexOffset, Authentication item, string serverUri, ILogger? logger = null)
    {
        using (ImRaii.Disabled(_discordOAuthUIDs == null))
        {
            var aliasPairs = _discordOAuthUIDs?.Result?.Select(t => new UIDAliasPair(t.Key, t.Value)).ToList() ?? [new UIDAliasPair(item.UID ?? null, null)];
            var uidComboName = "UID###" + item.CharacterName + item.WorldId + serverUri + indexOffset + aliasPairs.Count;
            DrawCombo(uidComboName, aliasPairs,
                (v) =>
                {
                    if (v is null)
                        return "No UID set";

                    if (!string.IsNullOrEmpty(v.Alias))
                    {
                        return $"{v.UID} ({v.Alias})";
                    }

                    if (string.IsNullOrEmpty(v.UID))
                        return "No UID set";

                    return $"{v.UID}";
                },
                (v) =>
                {
                    if (!string.Equals(v?.UID ?? null, item.UID, StringComparison.Ordinal))
                    {
                        item.UID = v?.UID ?? null;
                        _serverConfigurationManager.Save();
                    }
                },
                aliasPairs.Find(f => string.Equals(f.UID, item.UID, StringComparison.Ordinal)) ?? default);
        }

        if (_discordOAuthUIDs == null)
        {
            AttachToolTip("Use the button above to update your UIDs from the service before you can assign UIDs to characters.");
        }
    }

    public void DrawUnlinkOAuthButton(ServerStorage selectedServer)
    {
        using (ImRaii.Disabled(!CtrlPressed()))
        {
            if (IconTextButton(FontAwesomeIcon.Trash, "Unlink OAuth2 Connection") && UiSharedService.CtrlPressed())
            {
                selectedServer.OAuthToken = null;
                _serverConfigurationManager.Save();
                ResetOAuthTasksState();
            }
        }
        DrawHelpText("Hold CTRL to unlink the current OAuth2 connection.");
    }

    public void DrawUpdateOAuthUIDsButton(ServerStorage selectedServer)
    {
        if (!selectedServer.UseOAuth2)
            return;

        using (ImRaii.Disabled(string.IsNullOrEmpty(selectedServer.OAuthToken)))
        {
            if ((_discordOAuthUIDs == null || _discordOAuthUIDs.IsCompleted)
                && IconTextButton(FontAwesomeIcon.ArrowsSpin, "Update UIDs from Service")
                && !string.IsNullOrEmpty(selectedServer.OAuthToken))
            {
                _discordOAuthUIDs = _serverConfigurationManager.GetUIDsWithDiscordToken(selectedServer.ServerUri, selectedServer.OAuthToken);
            }
        }
        DateTime tokenExpiry = DateTime.MinValue;
        if (!string.IsNullOrEmpty(selectedServer.OAuthToken) && !_oauthTokenExpiry.TryGetValue(selectedServer.OAuthToken, out tokenExpiry))
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(selectedServer.OAuthToken);
                tokenExpiry = _oauthTokenExpiry[selectedServer.OAuthToken] = jwt.ValidTo;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not parse OAuth token, deleting");
                selectedServer.OAuthToken = null;
                _serverConfigurationManager.Save();
                tokenExpiry = DateTime.MinValue;
            }
        }
        if (string.IsNullOrEmpty(selectedServer.OAuthToken) || tokenExpiry < DateTime.UtcNow)
        {
            ColorTextWrapped("You have no OAuth token or the OAuth token is expired. Please use the Service Configuration to link your OAuth2 account or refresh the token.", ImGuiColors.DalamudRed);
        }
    }

    public Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGuiHelpers.GetButtonSize(icon.ToIconString());
    }

    public Vector2 GetIconSize(FontAwesomeIcon icon)
    {
        using var font = IconFont.Push();
        return ImGui.CalcTextSize(icon.ToIconString());
    }

    public float GetIconTextButtonSize(FontAwesomeIcon icon, string text)
    {
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());

        Vector2 vector2 = ImGui.CalcTextSize(text);
        float num = 3f * ImGuiHelpers.GlobalScale;
        return vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num;
    }

    public bool IconButton(FontAwesomeIcon icon, float? height = null)
    {
        string text = icon.ToIconString();

        ImGui.PushID(text);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float x = vector.X + ImGui.GetStyle().FramePadding.X * 2f;
        float frameHeight = height ?? ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X,
            cursorScreenPos.Y + (height ?? ImGui.GetFrameHeight()) / 2f - (vector.Y / 2f));
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();

        return result;
    }

    public void IconText(FontAwesomeIcon icon, uint color)
    {
        FontText(icon.ToIconString(), IconFont, color);
    }

    public void IconText(FontAwesomeIcon icon, Vector4? color = null)
    {
        IconText(icon, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    public bool IconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
    {
        return IconTextButtonInternal(icon, text,
            isInPopup ? ColorHelpers.RgbaUintToVector4(ImGui.GetColorU32(ImGuiCol.PopupBg)) : null,
            width <= 0 ? null : width);
    }

    public IDalamudTextureWrap LoadImage(byte[] imageData)
    {
        return _textureProvider.CreateFromImageAsync(imageData).Result;
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    internal static void DistanceSeparator()
    {
        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);
    }

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);

    internal void ResetOAuthTasksState()
    {
        _discordOAuthCheck = null;
        _discordOAuthGetCts = _discordOAuthGetCts.CancelRecreate();
        _discordOAuthGetCode = null;
        _discordOAuthUIDs = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        base.Dispose(disposing);

        UidFont.Dispose();
        GameFont.Dispose();
    }

    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript, 5000)]
    private static partial Regex PathRegex();

    private static void FontText(string text, IFontHandle font, Vector4? color = null)
    {
        FontText(text, font, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    private static void FontText(string text, IFontHandle font, uint color)
    {
        using var pushedFont = font.Push();
        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
    }

    private bool IconTextButtonInternal(FontAwesomeIcon icon, string text, Vector4? defaultColor = null, float? width = null)
    {
        int num = 0;
        if (defaultColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, defaultColor.Value);
            num++;
        }

        ImGui.PushID(text);
        Vector2 vector;
        using (IconFont.Push())
            vector = ImGui.CalcTextSize(icon.ToIconString());
        Vector2 vector2 = ImGui.CalcTextSize(text);
        ImDrawListPtr windowDrawList = ImGui.GetWindowDrawList();
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        float num2 = 3f * ImGuiHelpers.GlobalScale;
        float x = width ?? vector.X + vector2.X + ImGui.GetStyle().FramePadding.X * 2f + num2;
        float frameHeight = ImGui.GetFrameHeight();
        bool result = ImGui.Button(string.Empty, new Vector2(x, frameHeight));
        Vector2 pos = new Vector2(cursorScreenPos.X + ImGui.GetStyle().FramePadding.X, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        using (IconFont.Push())
            windowDrawList.AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        Vector2 pos2 = new Vector2(pos.X + vector.X + num2, cursorScreenPos.Y + ImGui.GetStyle().FramePadding.Y);
        windowDrawList.AddText(pos2, ImGui.GetColorU32(ImGuiCol.Text), text);
        ImGui.PopID();
        if (num > 0)
        {
            ImGui.PopStyleColor(num);
        }

        return result;
    }
    public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);
    private record UIDAliasPair(string? UID, string? Alias);
}