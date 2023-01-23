using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using MareSynchronos.Export;
using MareSynchronos.Utils;
using System;
using System.Threading.Tasks;

namespace MareSynchronos.UI;

public class GposeUi : Window, IDisposable
{
    private readonly WindowSystem _windowSystem;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly DalamudUtil _dalamudUtil;
    private readonly FileDialogManager _fileDialogManager;

    public GposeUi(WindowSystem windowSystem, MareCharaFileManager mareCharaFileManager, DalamudUtil dalamudUtil, FileDialogManager fileDialogManager) : base("Mare Synchronos Gpose Import UI###MareSynchronosGposeUI")
    {
        _windowSystem = windowSystem;
        _mareCharaFileManager = mareCharaFileManager;
        _dalamudUtil = dalamudUtil;
        _fileDialogManager = fileDialogManager;
        _dalamudUtil.GposeStart += StartGpose;
        _dalamudUtil.GposeEnd += EndGpose;
        IsOpen = _dalamudUtil.IsInGpose;
        _windowSystem.AddWindow(this);
    }

    private void EndGpose()
    {
        IsOpen = false;
    }

    private void StartGpose()
    {
        IsOpen = true;
    }

    public void Dispose()
    {
        _dalamudUtil.GposeStart -= StartGpose;
        _dalamudUtil.GposeEnd -= EndGpose;
        _windowSystem.RemoveWindow(this);
    }

    public override void Draw()
    {
        if (!_dalamudUtil.IsInGpose) IsOpen = false;

        UiShared.SetScaledWindowSize(300, 120, false);
        if (!_mareCharaFileManager.CurrentlyWorking)
        {
            if (UiShared.IconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF and apply"))
            {
                _fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", (success, path) =>
                {
                    if (!success) return;

                    Task.Run(() => _mareCharaFileManager.LoadMareCharaFile(path, _dalamudUtil.GposeTargetGameObject));
                });
            }
            UiShared.AttachToolTip("Applies it to the currently selected GPose actor");
        }
        else
        {
            UiShared.ColorTextWrapped("Loading Character...", ImGuiColors.DalamudYellow);
        }
        UiShared.TextWrapped("You can disable the automatic loading of this window in the Mare settings and open it manually with /mare gpose");
    }
}
