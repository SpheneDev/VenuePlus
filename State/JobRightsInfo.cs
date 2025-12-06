using System;

namespace VenuePlus.State;

public sealed class JobRightsInfo
{
    public bool AddVip { get; set; }
    public bool RemoveVip { get; set; }
    public bool ManageUsers { get; set; }
    public bool ManageJobs { get; set; }
    public bool EditVipDuration { get; set; }
    public bool AddDj { get; set; }
    public bool RemoveDj { get; set; }
    public int Rank { get; set; } = 1;
    public string ColorHex { get; set; } = "#FFFFFF";
    public string IconKey { get; set; } = "User";
}
