using System;

namespace VenuePlus.State;

public sealed class VipEntry
{
    public string CharacterName { get; set; } = string.Empty;
    public string HomeWorld { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public VipDuration Duration { get; set; } = VipDuration.FourWeeks;

    public string Key => CharacterName + "@" + HomeWorld;

    public bool IsExpired(DateTimeOffset nowUtc)
    {
        if (Duration == VipDuration.Lifetime)
        {
            return false;
        }

        if (!ExpiresAt.HasValue)
        {
            return false;
        }

        return ExpiresAt.Value <= nowUtc;
    }
}
