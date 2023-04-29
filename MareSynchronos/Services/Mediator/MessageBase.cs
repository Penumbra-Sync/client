namespace MareSynchronos.Services.Mediator;

public abstract record MessageBase
{
    public virtual bool KeepThreadContext => false;
}

public record SameThreadMessage : MessageBase
{
    public override bool KeepThreadContext => true;
}