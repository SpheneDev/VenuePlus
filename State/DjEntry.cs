using System;

namespace VenuePlus.State;

public sealed class DjEntry
{
    public string DjName { get; set; } = string.Empty;
    public string TwitchLink { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
