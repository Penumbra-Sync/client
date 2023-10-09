using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public abstract class PopupHandlerBase : WindowMediatorSubscriberBase
{
    protected readonly UiSharedService _uiSharedService;
    protected bool _openPopup = false;
    protected abstract Vector2 PopupSize { get; }
    protected PopupHandlerBase(string title, ILogger logger, MareMediator mediator, UiSharedService uiSharedService)
        : base(logger, mediator, title)
    {
        _uiSharedService = uiSharedService;
        Flags = ImGuiWindowFlags.NoBringToFrontOnFocus
          | ImGuiWindowFlags.NoDecoration
          | ImGuiWindowFlags.NoInputs
          | ImGuiWindowFlags.NoSavedSettings
          | ImGuiWindowFlags.NoBackground
          | ImGuiWindowFlags.NoMove
          | ImGuiWindowFlags.NoNav
          | ImGuiWindowFlags.NoTitleBar;
        IsOpen = true;
    }

    public override void Draw()
    {
        if (_openPopup)
        {
            ImGui.OpenPopup(WindowName);
            _openPopup = false;
        }

        DrawPopupInternal();
    }

    private void DrawPopupInternal()
    {
        var viewportSize = ImGui.GetWindowViewport().Size;
        ImGui.SetNextWindowSize(PopupSize);
        ImGui.SetNextWindowPos(viewportSize / 2, ImGuiCond.Always, new Vector2(0.5f));
        using var popup = ImRaii.Popup(WindowName, ImGuiWindowFlags.Modal);
        if (!popup) return;
        DrawContent();
    }

    protected abstract void DrawContent();
}
