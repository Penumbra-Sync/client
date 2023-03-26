using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.VM;

public abstract class WindowVMBase<T> : WindowMediatorSubscriberBase where T : ImguiVM
{
    protected readonly T VM;

    protected WindowVMBase(T vm, ILogger logger, MareMediator mediator, string name) : base(logger, mediator, name)
    {
        VM = vm;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Mediator.UnsubscribeAll(this);
    }

    protected Lazy<T1> GetLazyVM<T1>() where T1 : ImguiVM
    {
        return new Lazy<T1>(VM.As<T1>());
    }
}