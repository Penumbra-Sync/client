
namespace MareSynchronos.UI.Components;

public interface IDrawFolder
{
    int TotalPairs { get; }
    int OnlinePairs { get; }
    IEnumerable<DrawUserPair> DrawPairs { get; }

    void Draw();
}
