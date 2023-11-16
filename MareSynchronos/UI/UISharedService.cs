using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Utility;
using ImGuiNET;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Localization;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class UiSharedService : DisposableMediatorSubscriberBase
{
    public static readonly ImGuiWindowFlags PopupWindowFlags = ImGuiWindowFlags.NoResize |
                                           ImGuiWindowFlags.NoScrollbar |
                                           ImGuiWindowFlags.NoScrollWithMouse;

    public readonly FileDialogManager FileDialogManager;

    private const string _notesEnd = "##MARE_SYNCHRONOS_USER_NOTES_END##";

    private const string _notesStart = "##MARE_SYNCHRONOS_USER_NOTES_START##";

    private readonly ApiController _apiController;

    private readonly PeriodicFileScanner _cacheScanner;

    private readonly MareConfigService _configService;

    private readonly DalamudUtilService _dalamudUtil;
    private readonly IpcManager _ipcManager;
    private readonly Dalamud.Localization _localization;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, object> _selectedComboItems = new(StringComparer.Ordinal);
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private bool _cacheDirectoryHasOtherFilesThanCache = false;

    private bool _cacheDirectoryIsValidPath = true;

    private bool _customizePlusExists = false;

    private string _customServerName = "";

    private string _customServerUri = "";

    private bool _glamourerExists = false;

    private bool _heelsExists = false;

    private bool _honorificExists = false;
    private bool _isDirectoryWritable = false;

    private bool _isPenumbraDirectory = false;

    private bool _palettePlusExists = false;
    private bool _penumbraExists = false;

    private int _serverSelectionIndex = -1;

    public UiSharedService(ILogger<UiSharedService> logger, IpcManager ipcManager, ApiController apiController,
        PeriodicFileScanner cacheScanner, FileDialogManager fileDialogManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, DalamudPluginInterface pluginInterface, Dalamud.Localization localization,
        ServerConfigurationManager serverManager, MareMediator mediator) : base(logger, mediator)
    {
        _ipcManager = ipcManager;
        _apiController = apiController;
        _cacheScanner = cacheScanner;
        FileDialogManager = fileDialogManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pluginInterface;
        _localization = localization;
        _serverConfigurationManager = serverManager;

        _localization.SetupWithLangCode("en");

        _isDirectoryWritable = IsDirectoryWritable(_configService.Current.CacheFolder);

        _pluginInterface.UiBuilder.BuildFonts += BuildFont;
        _pluginInterface.UiBuilder.RebuildFonts();

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) =>
        {
            _penumbraExists = _ipcManager.CheckPenumbraApi();
            _glamourerExists = _ipcManager.CheckGlamourerApi();
            _customizePlusExists = _ipcManager.CheckCustomizePlusApi();
            _heelsExists = _ipcManager.CheckHeelsApi();
            _palettePlusExists = _ipcManager.CheckPalettePlusApi();
            _honorificExists = _ipcManager.CheckHonorificApi();
        });
    }

    public ApiController ApiController => _apiController;

    public bool EditTrackerPosition { get; set; }

    public long FileCacheSize => _cacheScanner.FileCacheSize;

    public bool HasValidPenumbraModPath => !(_ipcManager.PenumbraModDirectory ?? string.Empty).IsNullOrEmpty() && Directory.Exists(_ipcManager.PenumbraModDirectory);

    public bool IsInGpose => _dalamudUtil.IsInCutscene;

    public string PlayerName => _dalamudUtil.GetPlayerName();

    public ImFontPtr UidFont { get; private set; }

    public bool UidFontBuilt { get; private set; }

    public Dictionary<ushort, string> WorldData => _dalamudUtil.WorldData.Value;

    public uint WorldId => _dalamudUtil.GetWorldId();

    public const string TooltipSeparator = "--SEP--";

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

    public static void BooleanToColoredIcon(bool value, bool inline = true)
    {
        using var colorgreen = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, value);
        using var colorred = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed, !value);

        if (inline) ImGui.SameLine();

        if (value)
        {
            NormalizedIcon(FontAwesomeIcon.Check);
        }
        else
        {
            NormalizedIcon(FontAwesomeIcon.Times);
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

    public static void ColorTextWrapped(string text, Vector4 color)
    {
        using var raiicolor = ImRaii.PushColor(ImGuiCol.Text, color);
        TextWrapped(text);
    }

    public static bool CtrlPressed() => (GetKeyState(0xA2) & 0x8000) != 0 || (GetKeyState(0xA3) & 0x8000) != 0;

    public static void DrawHelpText(string helpText)
    {
        ImGui.SameLine();
        NormalizedIcon(FontAwesomeIcon.QuestionCircle, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        AttachToolTip(helpText);
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

    public static void FontText(string text, ImFontPtr font, Vector4? color = null)
    {
        using var pushedFont = ImRaii.PushFont(font);
        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, Color(color ?? new Vector4(1, 1, 1, 1)), color != null);
        ImGui.TextUnformatted(text);
    }

    public static Vector4 GetBoolColor(bool input) => input ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;

    public static Vector2 GetIconButtonSize(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        var buttonSize = ImGuiHelpers.GetButtonSize(icon.ToIconString());
        return buttonSize;
    }

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

    public static Vector2 GetNormalizedIconTextButtonSize(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
    {
        var iconData = GetIconData(icon);
        var textSize = ImGui.CalcTextSize(text);
        var padding = ImGui.GetStyle().FramePadding;
        var buttonSizeY = ImGui.GetFrameHeight();
        var iconExtraSpacing = isInPopup ? padding.X * 2 : 0;

        if (width == null || width <= 0)
        {
            var buttonSizeX = iconData.NormalizedIconScale.X + (padding.X * 3) + iconExtraSpacing + textSize.X;
            return new Vector2(buttonSizeX, buttonSizeY);
        }
        else
        {
            return new Vector2(width.Value, buttonSizeY);
        }
    }

    public static Vector2 NormalizedIconButtonSize(FontAwesomeIcon icon)
    {
        var iconData = GetIconData(icon);
        var padding = ImGui.GetStyle().FramePadding;

        return iconData.NormalizedIconScale with { X = iconData.NormalizedIconScale.X + padding.X * 2, Y = iconData.NormalizedIconScale.Y + padding.Y * 2 };
    }

    public static bool NormalizedIconButton(FontAwesomeIcon icon)
    {
        bool wasClicked = false;
        var iconData = GetIconData(icon);
        var padding = ImGui.GetStyle().FramePadding;
        var cursor = ImGui.GetCursorPos();
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var scrollPosY = ImGui.GetScrollY();
        var scrollPosX = ImGui.GetScrollX();

        var buttonSize = NormalizedIconButtonSize(icon);

        if (ImGui.Button("###" + icon.ToIconString(), buttonSize))
        {
            wasClicked = true;
        }

        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * iconData.IconScaling,
            new(pos.X - scrollPosX + cursor.X + iconData.OffsetX + padding.X,
                pos.Y - scrollPosY + cursor.Y + (buttonSize.Y - (iconData.IconSize.Y * iconData.IconScaling)) / 2f),
            ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());

        return wasClicked;
    }

    public static bool NormalizedIconTextButton(FontAwesomeIcon icon, string text, float? width = null, bool isInPopup = false)
    {
        var wasClicked = false;

        var iconData = GetIconData(icon);
        var textSize = ImGui.CalcTextSize(text);
        var padding = ImGui.GetStyle().FramePadding;
        var cursor = ImGui.GetCursorPos();
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var scrollPosY = ImGui.GetScrollY();
        var scrollPosX = ImGui.GetScrollX();

        Vector2 buttonSize = GetNormalizedIconTextButtonSize(icon, text, width, isInPopup);
        var iconExtraSpacing = isInPopup ? padding.X * 2 : 0;

        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.PopupBg), isInPopup))
        {
            if (ImGui.Button("###" + icon.ToIconString() + text, buttonSize))
            {
                wasClicked = true;
            }
        }

        drawList.AddText(UiBuilder.DefaultFont, ImGui.GetFontSize(),
            new(pos.X - scrollPosX + cursor.X + iconData.NormalizedIconScale.X + (padding.X * 2) + iconExtraSpacing,
               pos.Y - scrollPosY + cursor.Y + ((buttonSize.Y - textSize.Y) / 2f)),
            ImGui.GetColorU32(ImGuiCol.Text), text);

        drawList.AddText(UiBuilder.IconFont, ImGui.GetFontSize() * iconData.IconScaling,
            new(pos.X - scrollPosX + cursor.X + iconData.OffsetX + padding.X,
                pos.Y - scrollPosY + cursor.Y + (buttonSize.Y - (iconData.IconSize.Y * iconData.IconScaling)) / 2f),
            ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());

        return wasClicked;
    }

    public static void NormalizedIcon(FontAwesomeIcon icon, uint color)
    {
        var cursorPos = ImGui.GetCursorPos();
        var iconData = GetIconData(icon);
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var scrollPosX = ImGui.GetScrollX();
        var scrollPosY = ImGui.GetScrollY();
        var frameHeight = ImGui.GetFrameHeight();

        var frameOffsetY = ((frameHeight - iconData.IconSize.Y * iconData.IconScaling) / 2f);

        drawList.AddText(UiBuilder.IconFont, UiBuilder.IconFont.FontSize * iconData.IconScaling,
            new(windowPos.X - scrollPosX + cursorPos.X + iconData.OffsetX,
            windowPos.Y - scrollPosY + cursorPos.Y + frameOffsetY),
            color, icon.ToIconString());

        ImGui.Dummy(new(iconData.NormalizedIconScale.X, ImGui.GetFrameHeight()));
    }

    public static void NormalizedIcon(FontAwesomeIcon icon, Vector4? color = null)
    {
        NormalizedIcon(icon, color == null ? ImGui.GetColorU32(ImGuiCol.Text) : ImGui.GetColorU32(color.Value));
    }

    private static IconScaleData CalcIconScaleData(FontAwesomeIcon icon)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(icon.ToIconString());
        var iconscaling = (iconSize.X < iconSize.Y ? (iconSize.Y - iconSize.X) / 2f : 0f, iconSize.X > iconSize.Y ? 1f / (iconSize.X / iconSize.Y) : 1f);
        var normalized = iconscaling.Item2 == 1f ?
            new Vector2(iconSize.Y, iconSize.Y)
            : new((iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2), (iconSize.X * iconscaling.Item2) + (iconscaling.Item1 * 2));
        return new(iconSize, normalized, iconscaling.Item1, iconscaling.Item2);
    }

    public static IconScaleData GetIconData(FontAwesomeIcon icon)
    {
        if (_iconData.TryGetValue(ImGuiHelpers.GlobalScale, out var iconCache))
        {
            if (iconCache.TryGetValue(icon, out var iconData)) return iconData;
            return iconCache[icon] = CalcIconScaleData(icon);
        }

        _iconData.Add(ImGuiHelpers.GlobalScale, new());
        return _iconData[ImGuiHelpers.GlobalScale][icon] = CalcIconScaleData(icon);
    }

    public sealed record IconScaleData(Vector2 IconSize, Vector2 NormalizedIconScale, float OffsetX, float IconScaling);
    private static Dictionary<float, Dictionary<FontAwesomeIcon, IconScaleData>> _iconData = new();

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

    public static void TextWrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
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

    public void BigText(string text)
    {
        using var font = ImRaii.PushFont(UidFont, UidFontBuilt);
        ImGui.TextUnformatted(text);
    }

    public void DrawCacheDirectorySetting()
    {
        ColorTextWrapped("Note: The storage folder should be somewhere close to root (i.e. C:\\MareStorage) in a new empty folder. DO NOT point this to your game folder. DO NOT point this to your Penumbra folder.", ImGuiColors.DalamudYellow);
        var cacheDirectory = _configService.Current.CacheFolder;
        ImGui.InputText("Storage Folder##cache", ref cacheDirectory, 255, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        string folderIcon = FontAwesomeIcon.Folder.ToIconString();
        if (ImGui.Button(folderIcon + "##chooseCacheFolder"))
        {
            FileDialogManager.OpenFolderDialog("Pick Mare Synchronos Storage Folder", (success, path) =>
            {
                if (!success) return;

                _isPenumbraDirectory = string.Equals(path.ToLowerInvariant(), _ipcManager.PenumbraModDirectory?.ToLowerInvariant(), StringComparison.Ordinal);
                _isDirectoryWritable = IsDirectoryWritable(path);
                _cacheDirectoryHasOtherFilesThanCache = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any(f => Path.GetFileNameWithoutExtension(f).Length != 40);
                _cacheDirectoryIsValidPath = PathRegex().IsMatch(path);

                if (!string.IsNullOrEmpty(path)
                    && Directory.Exists(path)
                    && _isDirectoryWritable
                    && !_isPenumbraDirectory
                    && !_cacheDirectoryHasOtherFilesThanCache
                    && _cacheDirectoryIsValidPath)
                {
                    _configService.Current.CacheFolder = path;
                    _configService.Save();
                    _cacheScanner.StartScan();
                }
            });
        }
        ImGui.PopFont();

        if (_isPenumbraDirectory)
        {
            ColorTextWrapped("Do not point the storage path directly to the Penumbra directory. If necessary, make a subfolder in it.", ImGuiColors.DalamudRed);
        }
        else if (!_isDirectoryWritable)
        {
            ColorTextWrapped("The folder you selected does not exist or cannot be written to. Please provide a valid path.", ImGuiColors.DalamudRed);
        }
        else if (_cacheDirectoryHasOtherFilesThanCache)
        {
            ColorTextWrapped("Your selected directory has files inside that are not Mare related. Use an empty directory or a previous Mare storage directory only.", ImGuiColors.DalamudRed);
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

    public T? DrawCombo<T>(string comboName, IEnumerable<T> comboItems, Func<T, string> toName,
        Action<T>? onSelected = null, T? initialSelectedItem = default)
    {
        if (!comboItems.Any()) return default;

        if (!_selectedComboItems.TryGetValue(comboName, out var selectedItem) && selectedItem == null)
        {
            if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
            {
                selectedItem = initialSelectedItem;
                _selectedComboItems[comboName] = selectedItem!;
                if (!EqualityComparer<T>.Default.Equals(initialSelectedItem, default))
                    onSelected?.Invoke(initialSelectedItem);
            }
            else
            {
                selectedItem = comboItems.First();
                _selectedComboItems[comboName] = selectedItem!;
            }
        }

        if (ImGui.BeginCombo(comboName, toName((T)selectedItem!)))
        {
            foreach (var item in comboItems)
            {
                bool isSelected = EqualityComparer<T>.Default.Equals(item, (T)selectedItem);
                if (ImGui.Selectable(toName(item), isSelected))
                {
                    _selectedComboItems[comboName] = item!;
                    onSelected?.Invoke(item!);
                }
            }

            ImGui.EndCombo();
        }

        return (T)_selectedComboItems[comboName];
    }

    public void DrawFileScanState()
    {
        ImGui.TextUnformatted("File Scanner Status");
        ImGui.SameLine();
        if (_cacheScanner.IsScanRunning)
        {
            ImGui.TextUnformatted("Scan is running");
            ImGui.TextUnformatted("Current Progress:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_cacheScanner.TotalFiles == 1
                ? "Collecting files"
                : $"Processing {_cacheScanner.CurrentFileProgress}/{_cacheScanner.TotalFilesStorage} from storage ({_cacheScanner.TotalFiles} scanned in)");
            AttachToolTip("Note: it is possible to have more files in storage than scanned in, " +
                "this is due to the scanner normally ignoring those files but the game loading them in and using them on your character, so they get " +
                "added to the local storage.");
        }
        else if (_configService.Current.FileScanPaused)
        {
            ImGui.TextUnformatted("File scanner is paused");
            ImGui.SameLine();
            if (ImGui.Button("Force Rescan##forcedrescan"))
            {
                _cacheScanner.InvokeScan(forced: true);
            }
        }
        else if (_cacheScanner.HaltScanLocks.Any(f => f.Value > 0))
        {
            ImGui.TextUnformatted("Halted (" + string.Join(", ", _cacheScanner.HaltScanLocks.Where(f => f.Value > 0).Select(locker => locker.Key + ": " + locker.Value + " halt requests")) + ")");
            ImGui.SameLine();
            if (ImGui.Button("Reset halt requests##clearlocks"))
            {
                _cacheScanner.ResetLocks();
            }
        }
        else
        {
            ImGui.TextUnformatted("Next scan in " + _cacheScanner.TimeUntilNextScan);
        }
    }

    public bool DrawOtherPluginState()
    {
        var penumbraColor = _penumbraExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var glamourerColor = _glamourerExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var heelsColor = _heelsExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var customizeColor = _customizePlusExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var paletteColor = _palettePlusExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var honorificColor = _honorificExists ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        var check = FontAwesomeIcon.Check.ToIconString();
        var cross = FontAwesomeIcon.SquareXmark.ToIconString();
        ImGui.TextUnformatted("Mandatory Plugins:");

        ImGui.SameLine();
        ImGui.TextUnformatted("Penumbra");
        ImGui.SameLine();
        FontText(_penumbraExists ? check : cross, UiBuilder.IconFont, penumbraColor);
        ImGui.SameLine();
        AttachToolTip($"Penumbra is " + (_penumbraExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Glamourer");
        ImGui.SameLine();
        FontText(_glamourerExists ? check : cross, UiBuilder.IconFont, glamourerColor);
        ImGui.SameLine();
        AttachToolTip($"Glamourer is " + (_glamourerExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.TextUnformatted("Optional Plugins:");
        ImGui.SameLine();
        ImGui.TextUnformatted("SimpleHeels");
        ImGui.SameLine();
        FontText(_heelsExists ? check : cross, UiBuilder.IconFont, heelsColor);
        ImGui.SameLine();
        AttachToolTip($"SimpleHeels is " + (_heelsExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Customize+");
        ImGui.SameLine();
        FontText(_customizePlusExists ? check : cross, UiBuilder.IconFont, customizeColor);
        ImGui.SameLine();
        AttachToolTip($"Customize+ is " + (_customizePlusExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Palette+");
        ImGui.SameLine();
        FontText(_palettePlusExists ? check : cross, UiBuilder.IconFont, paletteColor);
        ImGui.SameLine();
        AttachToolTip($"Palette+ is " + (_palettePlusExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        ImGui.SameLine();
        ImGui.TextUnformatted("Honorific");
        ImGui.SameLine();
        FontText(_honorificExists ? check : cross, UiBuilder.IconFont, honorificColor);
        ImGui.SameLine();
        AttachToolTip($"Honorific is " + (_honorificExists ? "available and up to date." : "unavailable or not up to date."));
        ImGui.Spacing();

        if (!_penumbraExists || !_glamourerExists)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "You need to install both Penumbra and Glamourer and keep them up to date to use Mare Synchronos.");
            return false;
        }

        return true;
    }

    public int DrawServiceSelection(bool selectOnChange = false)
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

        if (_serverConfigurationManager.GetSecretKey(_serverSelectionIndex) != null)
        {
            ImGui.SameLine();
            var text = "Connect";
            if (_serverSelectionIndex == _serverConfigurationManager.CurrentServerIndex) text = "Reconnect";
            if (NormalizedIconTextButton(FontAwesomeIcon.Link, text))
            {
                _serverConfigurationManager.SelectServer(_serverSelectionIndex);
                _ = _apiController.CreateConnections();
            }
        }

        if (ImGui.TreeNode("Add Custom Service"))
        {
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("Custom Service URI", ref _customServerUri, 255);
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("Custom Service Name", ref _customServerName, 255);
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Plus, "Add Custom Service")
                && !string.IsNullOrEmpty(_customServerUri)
                && !string.IsNullOrEmpty(_customServerName))
            {
                _serverConfigurationManager.AddServer(new ServerStorage()
                {
                    ServerName = _customServerName,
                    ServerUri = _customServerUri,
                });
                _customServerName = string.Empty;
                _customServerUri = string.Empty;
                _configService.Save();
            }
            ImGui.TreePop();
        }

        return _serverSelectionIndex;
    }

    public void DrawTimeSpanBetweenScansSetting()
    {
        var timeSpan = _configService.Current.TimeSpanBetweenScansInSeconds;
        if (ImGui.SliderInt("Seconds between scans##timespan", ref timeSpan, 20, 60))
        {
            _configService.Current.TimeSpanBetweenScansInSeconds = timeSpan;
            _configService.Save();
        }
        DrawHelpText("This is the time in seconds between file scans. Increase it to reduce system load. A too high setting can cause issues when manually fumbling about in the cache or Penumbra mods folders.");
        var isPaused = _configService.Current.FileScanPaused;
        if (ImGui.Checkbox("Pause periodic file scan##filescanpause", ref isPaused))
        {
            _configService.Current.FileScanPaused = isPaused;
            _configService.Save();
        }
        DrawHelpText("This allows you to stop the periodic scans of your Penumbra and Mare cache directories. Use this to move the Mare cache and Penumbra mod folders around. If you enable this permanently, run a Force rescan after adding mods to Penumbra.");
    }

    public void LoadLocalization(string languageCode)
    {
        _localization.SetupWithLangCode(languageCode);
        Strings.ToS = new Strings.ToSStrings();
    }

    public void RecalculateFileCacheSize()
    {
        _cacheScanner.InvokeScan(forced: true);
    }

    [LibraryImport("user32")]
    internal static partial short GetKeyState(int nVirtKey);

    internal ImFontPtr GetGameFontHandle()
    {
        return _pluginInterface.UiBuilder.GetGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12)).ImFont;
    }

    internal IDalamudTextureWrap LoadImage(byte[] imageData)
    {
        return _pluginInterface.UiBuilder.LoadImage(imageData);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _pluginInterface.UiBuilder.BuildFonts -= BuildFont;
    }

    private static void CenterWindow(float width, float height, ImGuiCond cond = ImGuiCond.None)
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetWindowPos(new Vector2(center.X - width / 2, center.Y - height / 2), cond);
    }

#pragma warning disable MA0009 // Add regex evaluation timeout
    [GeneratedRegex(@"^(?:[a-zA-Z]:\\[\w\s\-\\]+?|\/(?:[\w\s\-\/])+?)$", RegexOptions.ECMAScript)]
#pragma warning restore MA0009 // Add regex evaluation timeout
    private static partial Regex PathRegex();

    private void BuildFont()
    {
        var fontFile = Path.Combine(_pluginInterface.DalamudAssetDirectory.FullName, "UIRes", "NotoSansCJKjp-Medium.otf");
        UidFontBuilt = false;

        if (File.Exists(fontFile))
        {
            try
            {
                UidFont = ImGui.GetIO().Fonts.AddFontFromFileTTF(fontFile, 35);
                UidFontBuilt = true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Font failed to load. {fontFile}", fontFile);
            }
        }
        else
        {
            Logger.LogDebug("Font doesn't exist. {fontFile}", fontFile);
        }
    }
}