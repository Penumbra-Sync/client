using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.VM;

public abstract class WindowElementVMBase<T> : DisposableMediatorSubscriberBase where T : ImguiVM
{
    protected readonly T VM;

    protected WindowElementVMBase(T vm, ILogger logger, MareMediator mediator) : base(logger, mediator)
    {
        VM = vm;
    }

    protected Lazy<T1> GetLazyVM<T1>() where T1 : ImguiVM
    {
        return new Lazy<T1>(VM.As<T1>());
    }
}