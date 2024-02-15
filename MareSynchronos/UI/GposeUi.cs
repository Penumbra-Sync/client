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
    private Task<long>? _expectedLength;
    private Task? _applicationTask;

    public GposeUi(ILogger<GposeUi> logger, MareCharaFileManager mareCharaFileManager,
        DalamudUtilService dalamudUtil, FileDialogManager fileDialogManager, MareConfigService configService,
        MareMediator mediator, PerformanceCollectorService performanceCollectorService) 
        : base(logger, mediator, "Mare Synchronos Gpose Import UI###MareSynchronosGposeUI", performanceCollectorService)
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

    protected override void DrawInternal()
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

                    _expectedLength = Task.Run(() => _mareCharaFileManager.LoadMareCharaFile(path));
                }, 1, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
            }
            UiSharedService.AttachToolTip("Applies it to the currently selected GPose actor");
            if (_mareCharaFileManager.LoadedCharaFile != null && _expectedLength != null)
            {
                UiSharedService.TextWrapped("Loaded file: " + _mareCharaFileManager.LoadedCharaFile.FilePath);
                UiSharedService.TextWrapped("File Description: " + _mareCharaFileManager.LoadedCharaFile.CharaFileData.Description);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Check, "Apply loaded MCDF"))
                {
                    _applicationTask = Task.Run(async () => await _mareCharaFileManager.ApplyMareCharaFile(_dalamudUtil.GposeTargetGameObject, _expectedLength!.GetAwaiter().GetResult()).ConfigureAwait(false));
                }
                UiSharedService.AttachToolTip("Applies it to the currently selected GPose actor");
                UiSharedService.ColorTextWrapped("Warning: redrawing or changing the character will revert all applied mods.", ImGuiColors.DalamudYellow);
            }
            if (_applicationTask?.IsFaulted ?? false)
            {
                UiSharedService.ColorTextWrapped("Failure to read MCDF file. MCDF file is possibly corrupt. Re-export the MCDF file and try again.",
                    ImGuiColors.DalamudRed);
                UiSharedService.ColorTextWrapped("Note: if this is your MCDF, try redrawing yourself, wait and re-export the file. " +
                    "If you received it from someone else have them do the same.", ImGuiColors.DalamudYellow);
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
        _applicationTask = null;
        _expectedLength = null;
        _mareCharaFileManager.ClearMareCharaFile();
    }

    private void StartGpose()
    {
        IsOpen = _configService.Current.OpenGposeImportOnGposeStart;
    }
}