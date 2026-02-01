using System;

namespace VenuePlus.State;

public sealed class StaffUser
{
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public string[] Jobs { get; set; } = Array.Empty<string>();
    public string Job { get; set; } = "Unassigned";
    public string Role { get; set; } = "power";
    public string Uid { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public bool IsOnline { get; set; }
}
