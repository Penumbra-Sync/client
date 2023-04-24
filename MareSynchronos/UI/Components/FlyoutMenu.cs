using Dalamud.Interface;
using Dalamud.Interface.Components;
using ImGuiNET;
using MareSynchronos.UI.Handlers;
using MareSynchronos.Utils;
using System.Numerics;

namespace MareSynchronos.UI.Components;

public class FlyoutMenu
{
    private readonly FlyoutMenuCommand _command;
    private readonly Dictionary<string, List<Button>> _menuEntries;
    private readonly ResettableLazy<Vector2> _sizeLazy;

    private FlyoutMenu(FlyoutMenuCommand command)
    {
        _command = command;
        _menuEntries = _command.MenuEntries.ToDictionary(c => c.Key, c => c.Value.Select(e => Button.FromCommand(e)).ToList(), StringComparer.Ordinal);
        _sizeLazy = new(() =>
        {
            try
            {
                ImGui.SetWindowFontScale(_command.Scale);

                return UiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
            }
            finally
            {
                ImGui.SetWindowFontScale(1f);
            }
        });
    }

    public Vector2 Size => _sizeLazy.Value;

    public static FlyoutMenu FromCommand(FlyoutMenuCommand command)
    {
        return new FlyoutMenu(command);
    }

    public void Draw()
    {
        ImGui.SetWindowFontScale(_command.Scale);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup(_command.CommandId);
        }
        ImGui.SetWindowFontScale(1f);

        if (ImGui.BeginPopup(_command.CommandId))
        {
            ImGui.PushID(_command.CommandId);
            var widthX = Enumerable.Max<float>(_menuEntries.SelectMany(k => k.Value).Where(k => k.Size != Vector2.Zero).Select(k => k.Size.X));

            var firstEntryY = _menuEntries.First(f => f.Value.Find(e => e.Size != Vector2.Zero) != null).Value[0].Size.Y;

            foreach (var entry in _menuEntries)
            {
                ImGui.TextUnformatted(entry.Key);
                foreach (var btn in entry.Value)
                {
                    btn.Draw(new(widthX, firstEntryY));
                }

                if (!string.Equals(_menuEntries.Last().Key, entry.Key, StringComparison.Ordinal))
                {
                    ImGui.Separator();
                }
            }
            ImGui.PopID();
            ImGui.EndPopup();
        }
    }
}