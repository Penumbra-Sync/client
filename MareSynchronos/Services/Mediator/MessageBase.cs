namespace MareSynchronos.Services.Mediator;

#pragma warning disable MA0048
public abstract record MessageBase
{
    public virtual bool KeepThreadContext => false;
}

public record SameThreadMessage : MessageBase
{
    public override bool KeepThreadContext => true;
}
#pragma warning restore MA0048