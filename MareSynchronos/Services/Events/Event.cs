using MareSynchronos.API.Data;

namespace MareSynchronos.Services.Events;

public record Event
{
    public DateTime EventTime { get; }
    public string UID { get; }
    public string Character { get; }
    public string EventSource { get; }
    public EventSeverity EventSeverity { get; }
    public string Message { get; }

    public Event(string? Character, UserData UserData, string EventSource, EventSeverity EventSeverity, string Message)
    {
        EventTime = DateTime.Now;
        this.UID = UserData.AliasOrUID;
        this.Character = Character ?? string.Empty;
        this.EventSource = EventSource;
        this.EventSeverity = EventSeverity;
        this.Message = Message;
    }

    public Event(UserData UserData, string EventSource, EventSeverity EventSeverity, string Message) : this(null, UserData, EventSource, EventSeverity, Message)
    {
    }

    public Event(string EventSource, EventSeverity EventSeverity, string Message)
        : this(new UserData(string.Empty), EventSource, EventSeverity, Message)
    {
    }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(UID))
            return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{(int)EventSeverity}}}\t{Message}";
        else
        {
            if (string.IsNullOrEmpty(Character))
                return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{(int)EventSeverity}}}\t<{UID}> {Message}";
            else
                return $"{EventTime:HH:mm:ss.fff}\t[{EventSource}]{{{(int)EventSeverity}}}\t<{UID}\\{Character}> {Message}";
        }
    }
}