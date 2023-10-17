namespace MareSynchronos.WebAPI.SignalR;

public class MareAuthFailureException : Exception
{
    public MareAuthFailureException(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}