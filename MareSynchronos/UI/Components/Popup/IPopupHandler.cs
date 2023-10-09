using MareSynchronos.Services.Mediator;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

public interface IPopupHandler
{
    Vector2 PopupSize { get; }
    void DrawContent();
}
