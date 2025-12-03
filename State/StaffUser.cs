using System;

namespace VenuePlus.State;

public sealed class StaffUser
{
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string Job { get; set; } = "Unassigned";
    public string Role { get; set; } = "power";
    public string Uid { get; set; } = string.Empty;
}
