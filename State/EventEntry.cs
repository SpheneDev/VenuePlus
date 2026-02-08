using System;

namespace VenuePlus.State;

public sealed class EventEntry
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset EndAt { get; set; } = DateTimeOffset.UtcNow.AddHours(4);
}
