namespace MareSynchronos.UI.VM;

public interface IImguiVM
{
    void ExecuteWithProp<T>(string nameOfProp, Func<T, T> act);
}