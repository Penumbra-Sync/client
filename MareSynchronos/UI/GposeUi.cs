using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class GposeUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareCharaFileManager _mareCharaFileManager;

    public GposeUi(ILogger<GposeUi> logger, MareCharaFileManager mareCharaFileManager,
        DalamudUtilService dalamudUtil, FileDialogManager fileDialogManager, MareConfigService configService,
        MareMediator mediator) : base(logger, mediator, "Mare Synchronos Gpose Import UI###MareSynchronosGposeUI")
    {
        _mareCharaFileManager = mareCharaFileManager;
        _dalamudUtil = dalamudUtil;
        _fileDialogManager = fileDialogManager;
        _configService = configService;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => StartGpose());
        Mediator.Subscribe<GposeEndMessage>(this, (_) => EndGpose());
        IsOpen = _dalamudUtil.IsInGpose;
        this.SizeConstraints = new()
        {
            MinimumSize = new(200, 200),
            MaximumSize = new(400, 400)
        };
    }

    public override void Draw()
    {
        if (!_dalamudUtil.IsInGpose) IsOpen = false;

        if (!_mareCharaFileManager.CurrentlyWorking)
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF"))
            {
                _fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", (success, paths) =>
                {
                    if (!success) return;
                    if (paths.FirstOrDefault() is not string path) return;

                    _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    _ = Task.Run(() => _mareCharaFileManager.LoadMareCharaFile(path));
                }, 1, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
            }
            UiSharedService.AttachToolTip("Applies it to the currently selected GPose actor");
            if (_mareCharaFileManager.LoadedCharaFile != null)
            {
                UiSharedService.TextWrapped("Loaded file: " + _mareCharaFileManager.LoadedCharaFile.FilePath);
                UiSharedService.TextWrapped("File Description: " + _mareCharaFileManager.LoadedCharaFile.CharaFileData.Description);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Check, "Apply loaded MCDF"))
                {
                    _ = Task.Run(async () => await _mareCharaFileManager.ApplyMareCharaFile(_dalamudUtil.GposeTargetGameObject).ConfigureAwait(false));
                }
                UiSharedService.AttachToolTip("Applies it to the currently selected GPose actor");
                UiSharedService.ColorTextWrapped("Warning: redrawing or changing the character will revert all applied mods.", ImGuiColors.DalamudYellow);
            }
        }
        else
        {
            UiSharedService.ColorTextWrapped("Loading Character...", ImGuiColors.DalamudYellow);
        }
        UiSharedService.TextWrapped("Hint: You can disable the automatic loading of this window in the Mare settings and open it manually with /mare gpose");
    }

    private void EndGpose()
    {
        IsOpen = false;
        _mareCharaFileManager.ClearMareCharaFile();
    }

    private void StartGpose()
    {
        IsOpen = _configService.Current.OpenGposeImportOnGposeStart;
    }
}